using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;

using BenchmarkDotNet.Attributes;

namespace Base58Encoding.Benchmarks;

// End-to-end 32/64-byte encode where the only difference between arms is the alphabet map -- the
// emit loop that turns raw base-58 digits into output characters. All three arms use the vectorised
// digit extraction, so this measures the map on top of it, which is the order issue #9 lists them in.
//
// Scalar  = the production per-digit table load.
// Shuffle = alphabet split into four 16-byte tables, four vpshufb lookups blended by digit >> 4.
//           Alphabet-agnostic.
// Range   = Bitcoin-specific: its alphabet is six runs of consecutive ASCII, so the character is
//           the digit plus an offset picked by five comparisons, with no table at all.
[MemoryDiagnoser]
[HideColumns("RatioSD")]
public class EndToEndAlphabetBenchmark
{
    private interface IMapMode { }
    private readonly struct BaselineMode : IMapMode { }
    private readonly struct ScalarMode : IMapMode { }
    private readonly struct ShuffleMode : IMapMode { }
    private readonly struct RangeMode : IMapMode { }

    [Params(32, 64)]
    public int Size { get; set; }

    private byte[] _data = default!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        _data = new byte[Size];
        rng.NextBytes(_data);
        _data[0] |= 1;

        byte[] expected = Encoding.ASCII.GetBytes(Base58.Bitcoin.Encode(_data));
        AssertMatches<BaselineMode>(expected);
        AssertMatches<ScalarMode>(expected);
        AssertMatches<ShuffleMode>(expected);
        AssertMatches<RangeMode>(expected);
    }

    private void AssertMatches<TMode>(byte[] expected)
        where TMode : struct, IMapMode
    {
        Span<byte> scratch = stackalloc byte[128];
        int len = EncodeFull<TMode>(_data, scratch);
        if (!scratch[..len].SequenceEqual(expected))
        {
            throw new InvalidOperationException($"EncodeFull<{typeof(TMode).Name}> does not match production Encode for size {Size}");
        }
    }

    // Both stages scalar: what master does today, so the other three arms read as the combined
    // effect of items 2 and 3 rather than item 3 alone.
    [Benchmark(Baseline = true)]
    public int BothScalar()
    {
        Span<byte> dest = stackalloc byte[128];
        return EncodeFull<BaselineMode>(_data, dest);
    }

    [Benchmark]
    public int ScalarMap()
    {
        Span<byte> dest = stackalloc byte[128];
        return EncodeFull<ScalarMode>(_data, dest);
    }

    [Benchmark]
    public int ShuffleMap()
    {
        Span<byte> dest = stackalloc byte[128];
        return EncodeFull<ShuffleMode>(_data, dest);
    }

    [Benchmark]
    public int RangeMap()
    {
        Span<byte> dest = stackalloc byte[128];
        return EncodeFull<RangeMode>(_data, dest);
    }

    private int EncodeFull<TMode>(ReadOnlySpan<byte> data, Span<byte> destination)
        where TMode : struct, IMapMode
        => data.Length == 32
            ? EncodeFull32<TMode>(data, destination)
            : EncodeFull64<TMode>(data, destination);

    [SkipLocalsInit]
    private static int EncodeFull32<TMode>(ReadOnlySpan<byte> data, Span<byte> destination)
        where TMode : struct, IMapMode
    {
        int inLeadingZeros = Base58.CountLeadingZeros(data);

        Span<uint> binary = stackalloc uint[Base58BitcoinTables.BinarySz32];
        for (int i = 0; i < Base58BitcoinTables.BinarySz32; i++)
        {
            binary[i] = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(i * sizeof(uint), sizeof(uint)));
        }

        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz32];
        intermediate.Clear();

        int rowLen = Base58BitcoinTables.IntermediateSz32 - 1;
        Span<ulong> acc = intermediate.Slice(1, rowLen);
        for (int i = 0; i < Base58BitcoinTables.BinarySz32; i++)
        {
            VectorMath.TensorMultiplyAdd(Base58BitcoinTables.EncodeTable32RowMajor.AsSpan(i * rowLen, rowLen), binary[i], acc);
        }

        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz32];
        return ReduceExtractEmit<TMode>(intermediate, rawBase58, inLeadingZeros, destination);
    }

    [SkipLocalsInit]
    private static int EncodeFull64<TMode>(ReadOnlySpan<byte> data, Span<byte> destination)
        where TMode : struct, IMapMode
    {
        int inLeadingZeros = Base58.CountLeadingZeros(data);

        Span<uint> binary = stackalloc uint[Base58BitcoinTables.BinarySz64];
        for (int i = 0; i < Base58BitcoinTables.BinarySz64; i++)
        {
            binary[i] = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(i * sizeof(uint), sizeof(uint)));
        }

        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz64];
        intermediate.Clear();

        int rowLen = Base58BitcoinTables.IntermediateSz64 - 1;
        Span<ulong> acc = intermediate.Slice(1, rowLen);
        for (int i = 0; i < 8; i++)
        {
            VectorMath.TensorMultiplyAdd(Base58BitcoinTables.EncodeTable64RowMajor.AsSpan(i * rowLen, rowLen), binary[i], acc);
        }

        intermediate[15] += intermediate[16] / Base58BitcoinTables.R1Div;
        intermediate[16] %= Base58BitcoinTables.R1Div;

        for (int i = 8; i < Base58BitcoinTables.BinarySz64; i++)
        {
            VectorMath.TensorMultiplyAdd(Base58BitcoinTables.EncodeTable64RowMajor.AsSpan(i * rowLen, rowLen), binary[i], acc);
        }

        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz64];
        return ReduceExtractEmit<TMode>(intermediate, rawBase58, inLeadingZeros, destination);
    }

    private static int ReduceExtractEmit<TMode>(
        Span<ulong> intermediate,
        Span<byte> rawBase58,
        int inLeadingZeros,
        Span<byte> destination)
        where TMode : struct, IMapMode
    {
        for (int i = intermediate.Length - 1; i > 0; i--)
        {
            intermediate[i - 1] += intermediate[i] / Base58BitcoinTables.R1Div;
            intermediate[i] %= Base58BitcoinTables.R1Div;
        }

        if (typeof(TMode) == typeof(BaselineMode))
        {
            for (int i = 0; i < intermediate.Length; i++)
            {
                uint v = (uint)intermediate[i];
                rawBase58[(5 * i) + 4] = (byte)(v % 58U);
                rawBase58[(5 * i) + 3] = (byte)(v / 58U % 58U);
                rawBase58[(5 * i) + 2] = (byte)(v / 3364U % 58U);
                rawBase58[(5 * i) + 1] = (byte)(v / 195112U % 58U);
                rawBase58[(5 * i) + 0] = (byte)(v / 11316496U);
            }
        }
        else
        {
            VectorMath.ExtractBase58Digits(intermediate, rawBase58);
        }

        int rawLeadingZeros = 0;
        while (rawLeadingZeros < rawBase58.Length && rawBase58[rawLeadingZeros] == 0)
        {
            rawLeadingZeros++;
        }

        int outputLength = inLeadingZeros + (rawBase58.Length - rawLeadingZeros);

        if (inLeadingZeros > 0)
        {
            destination[..inLeadingZeros].Fill((byte)'1');
        }

        // The only step that differs between arms.
        ReadOnlySpan<byte> digits = rawBase58[rawLeadingZeros..];
        Span<byte> target = destination[inLeadingZeros..];

        if (typeof(TMode) == typeof(ScalarMode) || typeof(TMode) == typeof(BaselineMode))
        {
            MapScalar(digits, target);
        }
        else if (typeof(TMode) == typeof(ShuffleMode))
        {
            MapShuffle(digits, target);
        }
        else
        {
            MapRange(digits, target);
        }

        return outputLength;
    }

    // ---- alphabet map arms ----------------------------------------------------------------------

    private static void MapScalar(ReadOnlySpan<byte> digits, Span<byte> destination)
    {
        ReadOnlySpan<byte> alphabet = BitcoinAlphabet.Characters;
        for (int i = 0; i < digits.Length; i++)
        {
            destination[i] = alphabet[digits[i]];
        }
    }

    private static void MapShuffle(ReadOnlySpan<byte> digits, Span<byte> destination)
    {
        ref byte src = ref MemoryMarshal.GetReference(digits);
        ref byte dst = ref MemoryMarshal.GetReference(destination);
        int len = digits.Length;
        int i = 0;

        if (Avx2.IsSupported && len >= Vector256<byte>.Count)
        {
            ReadOnlySpan<byte> alphabet = BitcoinAlphabet.Characters;
            Vector128<byte> s0 = Vector128.Create(alphabet[..16]);
            Vector128<byte> s1 = Vector128.Create(alphabet[16..32]);
            Vector128<byte> s2 = Vector128.Create(alphabet[32..48]);

            Span<byte> tail = stackalloc byte[16];
            tail.Clear();
            alphabet[48..].CopyTo(tail);
            Vector128<byte> s3 = Vector128.Create((ReadOnlySpan<byte>)tail);

            Vector256<byte> t0 = Vector256.Create(s0, s0);
            Vector256<byte> t1 = Vector256.Create(s1, s1);
            Vector256<byte> t2 = Vector256.Create(s2, s2);
            Vector256<byte> t3 = Vector256.Create(s3, s3);

            Vector256<byte> one = Vector256.Create((byte)1);
            Vector256<byte> two = Vector256.Create((byte)2);
            Vector256<byte> three = Vector256.Create((byte)3);
            Vector256<byte> lowNibble = Vector256.Create((byte)0x0F);

            int upper = len - Vector256<byte>.Count;
            for (; i <= upper; i += Vector256<byte>.Count)
            {
                Vector256<byte> d = Vector256.LoadUnsafe(ref src, (nuint)i);
                Vector256<byte> selector = Vector256.ShiftRightLogical(d.AsUInt16(), 4).AsByte() & lowNibble;

                Vector256<byte> mapped = Avx2.Shuffle(t0, d);
                mapped = Avx2.BlendVariable(mapped, Avx2.Shuffle(t1, d), Vector256.Equals(selector, one));
                mapped = Avx2.BlendVariable(mapped, Avx2.Shuffle(t2, d), Vector256.Equals(selector, two));
                mapped = Avx2.BlendVariable(mapped, Avx2.Shuffle(t3, d), Vector256.Equals(selector, three));

                mapped.StoreUnsafe(ref dst, (nuint)i);
            }
        }

        ReadOnlySpan<byte> table = BitcoinAlphabet.Characters;
        for (; i < len; i++)
        {
            Unsafe.Add(ref dst, i) = table[Unsafe.Add(ref src, i)];
        }
    }

    // Calls production directly so the measured arm and the shipped code cannot drift apart.
    private static void MapRange(ReadOnlySpan<byte> digits, Span<byte> destination)
        => VectorMath.MapBitcoinAlphabet(digits, destination);
}
