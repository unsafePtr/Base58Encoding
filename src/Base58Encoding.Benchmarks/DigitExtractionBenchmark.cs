using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

using BenchmarkDotNet.Attributes;

namespace Base58Encoding.Benchmarks;

// Digit extraction in isolation -- firedancer's intermediate_to_raw, the stage that turns each
// base-58^5 limb into five base-58 digits. Issue #9 calls the constant-division chain here the
// measured critical path of the 32/64-byte fast paths, so this measures the stage on its own
// before any of it is wired into production.
//
// Scalar      = the production chain of four constant divisions plus four mods per limb.
// SimdChained = four limbs per Vector256, one reciprocal (v/58) applied four times, so each
//               quotient feeds the next: one magic constant, dependency depth four.
// SimdWide    = four limbs per Vector256, four independent reciprocals (v/58, v/58^2, v/58^3,
//               v/58^4), so all four quotients issue in parallel: four magics, depth one.
//
// Both SIMD arms derive the digits by shifted subtraction rather than a second division, using
// q_k % 58 == q_k - 58 * q_(k-1), then pack a limb's five digits into the low five bytes of its
// lane so the store is one uint plus one byte instead of five byte stores.
[MemoryDiagnoser]
[HideColumns("RatioSD")]
public class DigitExtractionBenchmark
{
    // floor(v / 58) == (v * Recip58) >> Recip58Shift for every v < 58^5. Exact because
    // ceil(2^35 / 58) * 58 - 2^35 == 46 and (58^5 - 1) * 46 < 2^35.
    private const ulong Recip58 = 592409283UL;
    private const int Recip58Shift = 35;

    // 9 = IntermediateSz32 (45 digits), 18 = IntermediateSz64 (90 digits).
    [Params(9, 18)]
    public int Limbs { get; set; }

    private ulong[] _intermediate = default!;
    private byte[] _raw = default!;
    private byte[] _expected = default!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        _intermediate = new ulong[Limbs];
        for (int i = 0; i < Limbs; i++)
        {
            _intermediate[i] = (ulong)rng.NextInt64(Base58Pow5);
        }

        _raw = new byte[Limbs * 5];
        _expected = new byte[Limbs * 5];
        Scalar(_intermediate, _expected);

        Verify(SimdChained, nameof(SimdChained));
        Verify(SimdWide, nameof(SimdWide));
    }

    private const long Base58Pow5 = 656356768L;

    private delegate void Extractor(ReadOnlySpan<ulong> intermediate, Span<byte> raw);

    private void Verify(Extractor extractor, string name)
    {
        Array.Clear(_raw);
        extractor(_intermediate, _raw);
        if (!_raw.AsSpan().SequenceEqual(_expected))
        {
            throw new InvalidOperationException($"{name} disagrees with the scalar extraction at {Limbs} limbs");
        }
    }

    [Benchmark(Baseline = true)]
    public void ScalarArm() => Scalar(_intermediate, _raw);

    [Benchmark]
    public void SimdChainedArm() => SimdChained(_intermediate, _raw);

    [Benchmark]
    public void SimdWideArm() => SimdWide(_intermediate, _raw);

    // ---- production algorithm ------------------------------------------------------------------

    private static void Scalar(ReadOnlySpan<ulong> intermediate, Span<byte> raw)
    {
        for (int i = 0; i < intermediate.Length; i++)
        {
            uint v = (uint)intermediate[i];
            raw[(5 * i) + 4] = (byte)(v % 58U);
            raw[(5 * i) + 3] = (byte)(v / 58U % 58U);
            raw[(5 * i) + 2] = (byte)(v / 3364U % 58U);
            raw[(5 * i) + 1] = (byte)(v / 195112U % 58U);
            raw[(5 * i) + 0] = (byte)(v / 11316496U);
        }
    }

    // ---- one reciprocal, applied four times -----------------------------------------------------

    private static void SimdChained(ReadOnlySpan<ulong> intermediate, Span<byte> raw)
    {
        ref ulong src = ref MemoryMarshal.GetReference(intermediate);
        ref byte dst = ref MemoryMarshal.GetReference(raw);
        int len = intermediate.Length;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && BitConverter.IsLittleEndian && len >= Vector256<ulong>.Count)
        {
            Vector256<ulong> m = Vector256.Create(Recip58);
            Vector256<ulong> c58 = Vector256.Create(58UL);
            int upper = len - Vector256<ulong>.Count;

            for (; i <= upper; i += Vector256<ulong>.Count)
            {
                Vector256<ulong> v = Vector256.LoadUnsafe(ref src, (nuint)i);

                Vector256<ulong> q3 = Vector256.ShiftRightLogical(VectorMath.MultiplyWidening32(v, m), Recip58Shift);
                Vector256<ulong> q2 = Vector256.ShiftRightLogical(VectorMath.MultiplyWidening32(q3, m), Recip58Shift);
                Vector256<ulong> q1 = Vector256.ShiftRightLogical(VectorMath.MultiplyWidening32(q2, m), Recip58Shift);
                Vector256<ulong> q0 = Vector256.ShiftRightLogical(VectorMath.MultiplyWidening32(q1, m), Recip58Shift);

                StorePacked(Pack(v, q3, q2, q1, q0, c58), ref dst, i);
            }
        }

        for (; i < len; i++)
        {
            ScalarLimb((uint)Unsafe.Add(ref src, i), ref dst, i);
        }
    }

    // ---- four independent reciprocals -----------------------------------------------------------

    // Magic constants for the remaining divisors, same construction as Recip58: multiplier
    // ceil(2^shift / divisor), shift chosen so the rounding error stays below one over the whole
    // 0 .. 58^5 domain. Every pair below was checked against the true quotient for all 656,356,768
    // legal limb values, so these are proven rather than argued from an error bound.
    private const ulong Recip3364 = 326846501UL;
    private const int Recip3364Shift = 40;
    private const ulong Recip195112 = 11270569UL;
    private const int Recip195112Shift = 41;
    private const ulong Recip11316496 = 795935355UL;
    private const int Recip11316496Shift = 53;

    private static void SimdWide(ReadOnlySpan<ulong> intermediate, Span<byte> raw)
    {
        ref ulong src = ref MemoryMarshal.GetReference(intermediate);
        ref byte dst = ref MemoryMarshal.GetReference(raw);
        int len = intermediate.Length;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && BitConverter.IsLittleEndian && len >= Vector256<ulong>.Count)
        {
            Vector256<ulong> m1 = Vector256.Create(Recip58);
            Vector256<ulong> m2 = Vector256.Create(Recip3364);
            Vector256<ulong> m3 = Vector256.Create(Recip195112);
            Vector256<ulong> m4 = Vector256.Create(Recip11316496);
            Vector256<ulong> c58 = Vector256.Create(58UL);
            int upper = len - Vector256<ulong>.Count;

            for (; i <= upper; i += Vector256<ulong>.Count)
            {
                Vector256<ulong> v = Vector256.LoadUnsafe(ref src, (nuint)i);

                Vector256<ulong> q3 = Vector256.ShiftRightLogical(VectorMath.MultiplyWidening32(v, m1), Recip58Shift);
                Vector256<ulong> q2 = Vector256.ShiftRightLogical(VectorMath.MultiplyWidening32(v, m2), Recip3364Shift);
                Vector256<ulong> q1 = Vector256.ShiftRightLogical(VectorMath.MultiplyWidening32(v, m3), Recip195112Shift);
                Vector256<ulong> q0 = Vector256.ShiftRightLogical(VectorMath.MultiplyWidening32(v, m4), Recip11316496Shift);

                StorePacked(Pack(v, q3, q2, q1, q0, c58), ref dst, i);
            }
        }

        for (; i < len; i++)
        {
            ScalarLimb((uint)Unsafe.Add(ref src, i), ref dst, i);
        }
    }

    // ---- shared tail --------------------------------------------------------------------------

    // q_k % 58 == q_k - 58 * q_(k-1), so the four low digits come from a subtract rather than a
    // second division. Each lane ends up holding its limb's five digits in the low five bytes,
    // most significant first, which is the order they are written in.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<ulong> Pack(
        Vector256<ulong> v,
        Vector256<ulong> q3,
        Vector256<ulong> q2,
        Vector256<ulong> q1,
        Vector256<ulong> q0,
        Vector256<ulong> c58)
    {
        Vector256<ulong> d4 = v - VectorMath.MultiplyWidening32(q3, c58);
        Vector256<ulong> d3 = q3 - VectorMath.MultiplyWidening32(q2, c58);
        Vector256<ulong> d2 = q2 - VectorMath.MultiplyWidening32(q1, c58);
        Vector256<ulong> d1 = q1 - VectorMath.MultiplyWidening32(q0, c58);

        return q0
               | Vector256.ShiftLeft(d1, 8)
               | Vector256.ShiftLeft(d2, 16)
               | Vector256.ShiftLeft(d3, 24)
               | Vector256.ShiftLeft(d4, 32);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StorePacked(Vector256<ulong> packed, ref byte dst, int limbIndex)
    {
        Span<ulong> lanes = stackalloc ulong[Vector256<ulong>.Count];
        packed.StoreUnsafe(ref MemoryMarshal.GetReference(lanes));

        for (int j = 0; j < Vector256<ulong>.Count; j++)
        {
            ulong p = lanes[j];
            int at = 5 * (limbIndex + j);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, at), (uint)p);
            Unsafe.Add(ref dst, at + 4) = (byte)(p >> 32);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ScalarLimb(uint v, ref byte dst, int limbIndex)
    {
        int at = 5 * limbIndex;
        Unsafe.Add(ref dst, at + 4) = (byte)(v % 58U);
        Unsafe.Add(ref dst, at + 3) = (byte)(v / 58U % 58U);
        Unsafe.Add(ref dst, at + 2) = (byte)(v / 3364U % 58U);
        Unsafe.Add(ref dst, at + 1) = (byte)(v / 195112U % 58U);
        Unsafe.Add(ref dst, at + 0) = (byte)(v / 11316496U);
    }
}
