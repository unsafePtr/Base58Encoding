using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Base58Encoding;

// Dot product and scaled add (axpy) for the 32/64-byte Bitcoin fast paths. The only file in the library
// that touches MemoryMarshal, Unsafe or hardware intrinsics, so that surface stays auditable in one
// place. Not nested in Base58<TAlphabet>: nothing here depends on the alphabet, and a nested type
// would be JIT-compiled once per instantiation.
internal static class VectorMath
{
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
                acc += Vector256.LoadUnsafe(ref xr, (nuint)i) * Vector256.LoadUnsafe(ref yr, (nuint)i);
            }

            sum += Vector256.Sum(acc);
        }
        else if (Vector128.IsHardwareAccelerated && len >= Vector128<ulong>.Count)
        {
            Vector128<ulong> acc = Vector128<ulong>.Zero;
            int upper = len - Vector128<ulong>.Count;
            for (; i <= upper; i += Vector128<ulong>.Count)
            {
                acc += Vector128.LoadUnsafe(ref xr, (nuint)i) * Vector128.LoadUnsafe(ref yr, (nuint)i);
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
                (a + (r * s)).StoreUnsafe(ref ar, (nuint)i);
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
                (a + (r * s)).StoreUnsafe(ref ar, (nuint)i);
            }
        }

        for (; i < len; i++)
        {
            Unsafe.Add(ref ar, i) += Unsafe.Add(ref rr, i) * scale;
        }
    }
}
