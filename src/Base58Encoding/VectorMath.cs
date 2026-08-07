using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Base58Encoding;

// Dot product and scaled add (axpy) for the 32/64-byte Bitcoin fast paths, plus the widening multiply
// they share. The only file in the library that touches MemoryMarshal, Unsafe or hardware intrinsics,
// so that surface stays auditable in one place. Not nested in Base58<TAlphabet>: nothing here depends
// on the alphabet, and a nested type would be JIT-compiled once per instantiation.
internal static class VectorMath
{
    /// <summary>
    /// Multiplies the low 32 bits of each 64-bit lane into a full 64-bit product.
    /// Both operands must fit in 32 bits; the assert enforces it.
    /// </summary>
    /// <remarks>
    /// AVX2 and NEON have no 64x64 multiply, so a portable <c>x * y</c> costs eight instructions. Every
    /// operand here is under 2^32 — limbs and encode entries are &lt; 58^5, decode entries &lt; 2^32,
    /// binary limbs are uint32 — so <c>vpmuludq</c> gives the identical answer in one. The JIT cannot
    /// substitute it: that needs proof the operands are narrow, and they come from a runtime-built table
    /// and a span. Purely a speed change; <c>x * y</c> was already correct.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector256<ulong> MultiplyWidening32(Vector256<ulong> x, Vector256<ulong> y)
    {
        Debug.Assert(
            (x & Vector256.Create(0xFFFFFFFF_00000000UL)) == Vector256<ulong>.Zero &&
            (y & Vector256.Create(0xFFFFFFFF_00000000UL)) == Vector256<ulong>.Zero,
            "MultiplyWidening32 requires both operands < 2^32; a wider value would be silently truncated.");

        // No arm64 branch here, unlike the Vector128 overload. Vector256.IsHardwareAccelerated is
        // never true on arm64: NEON registers are 128-bit, and SVE is vector-length agnostic so .NET
        // exposes it through its own API rather than mapping Vector256 onto it — the Neoverse-N2
        // probe reports sve2 in its CPU flags and the JIT still emits only the Vector128 length gate.
        // Both callers gate on that property, so arm64 never reaches this width. Note the type itself
        // is perfectly usable there; unguarded it would give identical results, just emulated as two
        // 128-bit halves. Accelerated or not, an AdvSimd branch here would never execute.
        return Avx2.IsSupported ? Avx2.Multiply(x.AsUInt32(), y.AsUInt32()) : x * y;
    }

    /// <inheritdoc cref="MultiplyWidening32(Vector256{ulong}, Vector256{ulong})"/>
    /// <remarks>
    /// arm64 hits the same wall as x64 for the opposite reason: NEON's <c>MUL</c> has no 64-bit form and
    /// <c>UMULL</c> takes 32-bit inputs, so the JIT cannot use it for a general 64x64 multiply either.
    /// Its fallback extracts each lane to a general-purpose register, uses the scalar <c>mul</c>, and
    /// reinserts — 8 instructions for 2 lanes, crossing the NEON/GPR domain four times (verified on a
    /// Neoverse-N2 via the arm64 codegen probe workflow). Because both operands fit in 32 bits, two
    /// <c>xtn</c> and one <c>umull</c> do the same work in the vector domain with no round trip.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<ulong> MultiplyWidening32(Vector128<ulong> x, Vector128<ulong> y)
    {
        Debug.Assert(
            (x & Vector128.Create(0xFFFFFFFF_00000000UL)) == Vector128<ulong>.Zero &&
            (y & Vector128.Create(0xFFFFFFFF_00000000UL)) == Vector128<ulong>.Zero,
            "MultiplyWidening32 requires both operands < 2^32; a wider value would be silently truncated.");

        if (Sse2.IsSupported)
        {
            return Sse2.Multiply(x.AsUInt32(), y.AsUInt32());
        }

        if (AdvSimd.IsSupported)
        {
            // xtn narrows each 64-bit lane to its low 32 bits, packing a vector into a Vector64<uint>;
            // lossless here because both operands are under 2^32. umull then widens lane-wise back to
            // 64-bit products. Three instructions, all in the vector domain.
            return AdvSimd.MultiplyWideningLower(
                AdvSimd.ExtractNarrowingLower(x),
                AdvSimd.ExtractNarrowingLower(y));
        }

        return x * y;
    }

    // Vectorized dot product of two equal-length ulong spans: sum(x[i] * y[i]).
    // A focused, dependency-free stand-in for TensorPrimitives.Dot<ulong>, tuned for the small
    // fixed-length decode columns (IntermediateSz32/64). Widest available width first, then a
    // scalar tail / fallback. The ulong multiply-accumulate wraps identically to the scalar loop,
    // so results are bit-for-bit the same.
    //
    // A fully bounds-checked (safe) rewrite is possible on .NET 11+ using the consume-and-advance
    // idiom — guard every span and advance by re-slicing:
    //     while (x.Length >= Vector256<ulong>.Count && y.Length >= Vector256<ulong>.Count)
    //     {
    //         acc += Vector256.Create(x) * Vector256.Create(y);
    //         x = x.Slice(Vector256<ulong>.Count);
    //         y = y.Slice(Vector256<ulong>.Count);
    //     }
    // On .NET 11 it JITs bounds-check-free and reaches parity on arm64, but on x64 the JIT still
    // emits a redundant second length guard per iteration (the spans are equal-length, but it can't
    // prove it), so it runs ~13-33% slower at the short lengths this kernel uses (9/18). On .NET 10
    // it is slower on every architecture. Staying on LoadUnsafe until the x64 check is elided.
    // Upstream tracking: https://github.com/dotnet/runtime/issues/127506 (removing unsafe from the
    // vectorization guidance). The x64 gap and the 13-33% figures are from the benchmark run in
    // https://github.com/EgorBot/Benchmarks/issues/401 (Zen 5 / Turin x64 vs Apple M1 arm64).
    internal static ulong TensorDot(ReadOnlySpan<ulong> x, ReadOnlySpan<ulong> y)
    {
        ref ulong xr = ref MemoryMarshal.GetReference(x);
        ref ulong yr = ref MemoryMarshal.GetReference(y);
        int len = x.Length;
        int i = 0;
        ulong sum = 0UL;

        if (Vector256.IsHardwareAccelerated && len >= Vector256<ulong>.Count)
        {
            Vector256<ulong> acc = Vector256<ulong>.Zero;
            int upper = len - Vector256<ulong>.Count;
            for (; i <= upper; i += Vector256<ulong>.Count)
            {
                acc += MultiplyWidening32(Vector256.LoadUnsafe(ref xr, (nuint)i), Vector256.LoadUnsafe(ref yr, (nuint)i));
            }

            sum += Vector256.Sum(acc);
        }
        else if (Vector128.IsHardwareAccelerated && len >= Vector128<ulong>.Count)
        {
            Vector128<ulong> acc = Vector128<ulong>.Zero;
            int upper = len - Vector128<ulong>.Count;
            for (; i <= upper; i += Vector128<ulong>.Count)
            {
                acc += MultiplyWidening32(Vector128.LoadUnsafe(ref xr, (nuint)i), Vector128.LoadUnsafe(ref yr, (nuint)i));
            }

            sum += Vector128.Sum(acc);
        }

        for (; i < len; i++)
        {
            sum += Unsafe.Add(ref xr, i) * Unsafe.Add(ref yr, i);
        }

        return sum;
    }

    // acc[k] += row[k] * scale over the whole row: multiply the row by a scalar and add into the
    // accumulator. The encode counterpart of TensorDot; mirrors TensorPrimitives.MultiplyAdd
    // (System.Numerics.Tensors) as a tiny dependency-free version tuned for the fixed-length encode
    // rows. Widest available vector width first, then a scalar tail that also serves as the fallback
    // when no width is hardware-accelerated. Wrapping ulong multiply-add in source-limb order, so the
    // result is bit-identical to the scalar loop.
    //
    // Kept on LoadUnsafe for the same reason as TensorDot — see the safe-rewrite note above.
    internal static void TensorMultiplyAdd(ReadOnlySpan<ulong> row, ulong scale, Span<ulong> acc)
    {
        ref ulong rr = ref MemoryMarshal.GetReference(row);
        ref ulong ar = ref MemoryMarshal.GetReference(acc);
        int len = row.Length;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && len >= Vector256<ulong>.Count)
        {
            Vector256<ulong> s = Vector256.Create(scale);
            int upper = len - Vector256<ulong>.Count;
            for (; i <= upper; i += Vector256<ulong>.Count)
            {
                Vector256<ulong> a = Vector256.LoadUnsafe(ref ar, (nuint)i);
                Vector256<ulong> r = Vector256.LoadUnsafe(ref rr, (nuint)i);
                (a + MultiplyWidening32(r, s)).StoreUnsafe(ref ar, (nuint)i);
            }
        }
        else if (Vector128.IsHardwareAccelerated && len >= Vector128<ulong>.Count)
        {
            Vector128<ulong> s = Vector128.Create(scale);
            int upper = len - Vector128<ulong>.Count;
            for (; i <= upper; i += Vector128<ulong>.Count)
            {
                Vector128<ulong> a = Vector128.LoadUnsafe(ref ar, (nuint)i);
                Vector128<ulong> r = Vector128.LoadUnsafe(ref rr, (nuint)i);
                (a + MultiplyWidening32(r, s)).StoreUnsafe(ref ar, (nuint)i);
            }
        }

        for (; i < len; i++)
        {
            Unsafe.Add(ref ar, i) += Unsafe.Add(ref rr, i) * scale;
        }
    }
}
