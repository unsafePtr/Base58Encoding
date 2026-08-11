using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

using BenchmarkDotNet.Attributes;

namespace Base58Encoding.Benchmarks;

// End-to-end 32/64-byte encode where the only difference between arms is digit extraction:
// the production chain of four constant divisions and four mods per limb, versus the lane-wise
// reciprocal kernel in VectorMath.ExtractBase58Digits.
//
// DigitExtractionBenchmark measures that stage on its own; this one answers whether the saving
// survives the surrounding pipeline. TMode is a struct marker so the typeof checks fold at JIT
// time and each arm compiles to exactly one of the two extraction bodies.
[MemoryDiagnoser]
[HideColumns("RatioSD")]
public class EndToEndExtractionBenchmark
{
    private interface IExtractMode { }
    private readonly struct ScalarMode : IExtractMode { }
    private readonly struct SimdMode : IExtractMode { }

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
        AssertMatches<ScalarMode>(expected);
        AssertMatches<SimdMode>(expected);
    }

    private void AssertMatches<TMode>(byte[] expected)
        where TMode : struct, IExtractMode
    {
        Span<byte> scratch = stackalloc byte[128];
        int len = EncodeFull<TMode>(_data, scratch);
        if (!scratch[..len].SequenceEqual(expected))
        {
            throw new InvalidOperationException($"EncodeFull<{typeof(TMode).Name}> does not match production Encode for size {Size}");
        }
    }

    [Benchmark(Baseline = true)]
    public int ScalarExtract()
    {
        Span<byte> dest = stackalloc byte[128];
        return EncodeFull<ScalarMode>(_data, dest);
    }

    [Benchmark]
    public int SimdExtract()
    {
        Span<byte> dest = stackalloc byte[128];
        return EncodeFull<SimdMode>(_data, dest);
    }

    private int EncodeFull<TMode>(ReadOnlySpan<byte> data, Span<byte> destination)
        where TMode : struct, IExtractMode
        => data.Length == 32
            ? EncodeFull32<TMode>(data, destination)
            : EncodeFull64<TMode>(data, destination);

    [SkipLocalsInit]
    private static int EncodeFull32<TMode>(ReadOnlySpan<byte> data, Span<byte> destination)
        where TMode : struct, IExtractMode
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
        where TMode : struct, IExtractMode
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
        where TMode : struct, IExtractMode
    {
        for (int i = intermediate.Length - 1; i > 0; i--)
        {
            intermediate[i - 1] += intermediate[i] / Base58BitcoinTables.R1Div;
            intermediate[i] %= Base58BitcoinTables.R1Div;
        }

        // The only step that differs between arms.
        if (typeof(TMode) == typeof(ScalarMode))
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

        int index = inLeadingZeros;
        ReadOnlySpan<byte> alphabet = BitcoinAlphabet.Characters;
        for (int k = rawLeadingZeros; k < rawBase58.Length; k++)
        {
            destination[index++] = alphabet[rawBase58[k]];
        }

        return outputLength;
    }
}
