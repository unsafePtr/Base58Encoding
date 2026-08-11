using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

using BenchmarkDotNet.Attributes;

namespace Base58Encoding.Benchmarks;

// The alphabet map -- firedancer's raw_to_base58 -- in isolation: raw base-58 digits to output
// characters. This is the emit loop of EncodeState, so the destination is UTF-16 char, which is
// what string.Create hands the encoder on the common Encode(data) API.
//
// Ceiling  = widen the digits straight into the destination with no alphabet lookup at all. Not a
//            correct encoder; it exists to bound how much the whole stage can possibly be worth.
// Scalar   = the production per-digit table load.
// Shuffle  = alphabet split into four 16-byte tables, four vpshufb lookups blended by digit >> 4.
//            Alphabet-agnostic, so it would work for Flickr and Ripple too.
// Range    = Bitcoin-specific. Its alphabet is six runs of consecutive ASCII, so the character is
//            the digit plus an offset chosen by five comparisons, with no table at all.
[MemoryDiagnoser]
[HideColumns("RatioSD")]
public class AlphabetMapBenchmark
{
    // 44 = digits emitted by a 32-byte encode, 88 = by a 64-byte encode.
    [Params(44, 88)]
    public int DigitCount { get; set; }

    private byte[] _digits = default!;
    private char[] _destination = default!;
    private char[] _expected = default!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        _digits = new byte[DigitCount];
        for (int i = 0; i < DigitCount; i++)
        {
            _digits[i] = (byte)rng.Next(58);
        }

        _destination = new char[DigitCount];
        _expected = new char[DigitCount];
        MapScalar(_digits, _expected);

        Verify(MapShuffle, nameof(MapShuffle));
        Verify(MapRange, nameof(MapRange));
        Verify(MapRange256Only, nameof(MapRange256Only));
    }

    private delegate void Mapper(ReadOnlySpan<byte> digits, Span<char> destination);

    private void Verify(Mapper mapper, string name)
    {
        Array.Clear(_destination);
        mapper(_digits, _destination);
        if (!_destination.AsSpan().SequenceEqual(_expected))
        {
            throw new InvalidOperationException($"{name} disagrees with the scalar alphabet map at {DigitCount} digits");
        }
    }

    [Benchmark]
    public void Ceiling() => MapNone(_digits, _destination);

    [Benchmark(Baseline = true)]
    public void Scalar() => MapScalar(_digits, _destination);

    [Benchmark]
    public void Shuffle() => MapShuffle(_digits, _destination);

    [Benchmark]
    public void Range() => MapRange(_digits, _destination);

    [Benchmark]
    public void Range256Only() => MapRange256Only(_digits, _destination);

    // ---- arms ----------------------------------------------------------------------------------

    // No alphabet at all: the widening store on its own, as a floor for the stage.
    private static void MapNone(ReadOnlySpan<byte> digits, Span<char> destination)
    {
        for (int i = 0; i < digits.Length; i++)
        {
            destination[i] = (char)digits[i];
        }
    }

    private static void MapScalar(ReadOnlySpan<byte> digits, Span<char> destination)
    {
        ReadOnlySpan<byte> alphabet = BitcoinAlphabet.Characters;
        for (int i = 0; i < digits.Length; i++)
        {
            destination[i] = (char)alphabet[digits[i]];
        }
    }

    // vpshufb indexes with the low four bits of each lane and zeroes the lane when bit 7 is set, so
    // four lookups against four 16-byte slices of the alphabet cover all 58 entries; digit >> 4
    // selects which one survives the blend. The table is duplicated into both 128-bit halves
    // because vpshufb never crosses the lane boundary.
    private static void MapShuffle(ReadOnlySpan<byte> digits, Span<char> destination)
    {
        ref byte src = ref MemoryMarshal.GetReference(digits);
        ref char dst = ref MemoryMarshal.GetReference(destination);
        int len = digits.Length;
        int i = 0;

        if (Avx2.IsSupported && len >= Vector256<byte>.Count)
        {
            ReadOnlySpan<byte> alphabet = BitcoinAlphabet.Characters;
            Vector256<byte> t0 = Vector256.Create(Vector128.Create(alphabet[..16]), Vector128.Create(alphabet[..16]));
            Vector256<byte> t1 = Vector256.Create(Vector128.Create(alphabet[16..32]), Vector128.Create(alphabet[16..32]));
            Vector256<byte> t2 = Vector256.Create(Vector128.Create(alphabet[32..48]), Vector128.Create(alphabet[32..48]));

            Span<byte> tail = stackalloc byte[16];
            tail.Clear();
            alphabet[48..].CopyTo(tail);
            Vector256<byte> t3 = Vector256.Create(Vector128.Create((ReadOnlySpan<byte>)tail), Vector128.Create((ReadOnlySpan<byte>)tail));

            Vector256<byte> one = Vector256.Create((byte)1);
            Vector256<byte> two = Vector256.Create((byte)2);
            Vector256<byte> three = Vector256.Create((byte)3);

            int upper = len - Vector256<byte>.Count;
            for (; i <= upper; i += Vector256<byte>.Count)
            {
                Vector256<byte> d = Vector256.LoadUnsafe(ref src, (nuint)i);
                Vector256<byte> selector = Vector256.ShiftRightLogical(d.AsUInt16(), 4).AsByte() & Vector256.Create((byte)0x0F);

                Vector256<byte> mapped = Avx2.Shuffle(t0, d);
                mapped = Avx2.BlendVariable(mapped, Avx2.Shuffle(t1, d), Vector256.Equals(selector, one));
                mapped = Avx2.BlendVariable(mapped, Avx2.Shuffle(t2, d), Vector256.Equals(selector, two));
                mapped = Avx2.BlendVariable(mapped, Avx2.Shuffle(t3, d), Vector256.Equals(selector, three));

                (Vector256<ushort> lo, Vector256<ushort> hi) = Vector256.Widen(mapped);
                lo.StoreUnsafe(ref Unsafe.As<char, ushort>(ref dst), (nuint)i);
                hi.StoreUnsafe(ref Unsafe.As<char, ushort>(ref dst), (nuint)(i + Vector256<ushort>.Count));
            }
        }

        ReadOnlySpan<byte> table = BitcoinAlphabet.Characters;
        for (; i < len; i++)
        {
            Unsafe.Add(ref dst, i) = (char)table[Unsafe.Add(ref src, i)];
        }
    }

    // The Bitcoin alphabet is '1'-'9', 'A'-'H', 'J'-'N', 'P'-'Z', 'a'-'k', 'm'-'z': six runs of
    // consecutive ASCII. So character == digit + 49 + 7*(d>8) + (d>16) + (d>21) + 6*(d>32) + (d>43),
    // and each comparison mask ANDed with its weight contributes that weight or nothing. Every digit
    // is under 58, so the signed byte compares are safe.
    // Calls production directly so the measured arm and the shipped code cannot drift apart.
    // Production runs a 256-bit loop, then a 128-bit loop over the remainder, then a scalar tail.
    private static void MapRange(ReadOnlySpan<byte> digits, Span<char> destination)
        => VectorMath.MapBitcoinAlphabet(digits, destination);

    // Same arithmetic, but the remainder after the 256-bit loop goes straight to the scalar tail
    // with no 128-bit pass. A 32-byte encode emits 44 digits, so that remainder is 12 -- the
    // 128-bit pass maps eight of them and leaves four. Whether that pays is the question: it has
    // to earn back a second loop's worth of setup over eight digits, and the two arms are here
    // rather than compared across runs because a four-nanosecond difference is exactly the size
    // of the run-to-run drift this machine shows.
    private static void MapRange256Only(ReadOnlySpan<byte> digits, Span<char> destination)
    {
        ref byte src = ref MemoryMarshal.GetReference(digits);
        ref char dst = ref MemoryMarshal.GetReference(destination);
        int len = digits.Length;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && len >= Vector256<byte>.Count)
        {
            for (; i <= len - Vector256<byte>.Count; i += Vector256<byte>.Count)
            {
                Vector256<sbyte> d = Vector256.LoadUnsafe(ref src, (nuint)i).AsSByte();

                Vector256<sbyte> mapped = d + Vector256.Create((sbyte)49)
                    + (Vector256.GreaterThan(d, Vector256.Create((sbyte)8)) & Vector256.Create((sbyte)7))
                    + (Vector256.GreaterThan(d, Vector256.Create((sbyte)16)) & Vector256.Create((sbyte)1))
                    + (Vector256.GreaterThan(d, Vector256.Create((sbyte)21)) & Vector256.Create((sbyte)1))
                    + (Vector256.GreaterThan(d, Vector256.Create((sbyte)32)) & Vector256.Create((sbyte)6))
                    + (Vector256.GreaterThan(d, Vector256.Create((sbyte)43)) & Vector256.Create((sbyte)1));

                (Vector256<ushort> lower, Vector256<ushort> upper) = Vector256.Widen(mapped.AsByte());
                lower.StoreUnsafe(ref Unsafe.As<char, ushort>(ref dst), (nuint)i);
                upper.StoreUnsafe(ref Unsafe.As<char, ushort>(ref dst), (nuint)(i + Vector256<ushort>.Count));
            }
        }

        ReadOnlySpan<byte> table = BitcoinAlphabet.Characters;
        for (; i < len; i++)
        {
            Unsafe.Add(ref dst, i) = (char)table[Unsafe.Add(ref src, i)];
        }
    }
}
