using System.Buffers.Binary;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Base58Encoding.Benchmarks;

// End-to-end 32/64-byte encode. Each arm runs the full encode path and differs only in the matmul
// multiply-add step (intermediate[1..] += binary[i] * EncodeTableRow[i]): hand-written SIMD (baseline) vs
// TensorPrimitives.MultiplyAdd, with the production scalar loop available as a commented reference.
// TMode is a struct marker, so the typeof checks fold at JIT time and each arm is fully specialized.
// Disasm: dotnet run -c Release -- --filter "*EndToEndEncode*" --disasm --disasmDepth 3
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
[HideColumns("RatioSD")]
public class EndToEndEncodeBenchmark
{
    // Empty value-type markers: one per matmul strategy. Constrained to `struct` so the JIT
    // specializes each generic instantiation and elides the typeof comparisons.
    private interface IMatmulMode { }
    private readonly struct ScalarMode : IMatmulMode { }
    private readonly struct SimdMode : IMatmulMode { }
    private readonly struct TensorMode : IMatmulMode { }

    [Params(32, 64)]
    public int Size { get; set; }

    private byte[] _data = default!;
    private uint[][] _jagged = default!;
    private ulong[] _rowMajor = default!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        _data = new byte[Size];
        rng.NextBytes(_data);

        // Scalar arm reads the production jagged tables directly; the vectorized arms read each
        // encode row laid out contiguously as ulong. Row length is IntermediateSz - 1 (the matmul
        // only touches columns 0..IntermediateSz-2, accumulating into intermediate[1..]).
        _jagged = Size == 32 ? Base58BitcoinTables.EncodeTable32 : Base58BitcoinTables.EncodeTable64;
        int binarySz = Size == 32 ? Base58BitcoinTables.BinarySz32 : Base58BitcoinTables.BinarySz64;
        int rowLen = (Size == 32 ? Base58BitcoinTables.IntermediateSz32 : Base58BitcoinTables.IntermediateSz64) - 1;

        _rowMajor = new ulong[binarySz * rowLen];
        for (int i = 0; i < binarySz; i++)
        {
            for (int j = 0; j < rowLen; j++)
            {
                _rowMajor[i * rowLen + j] = _jagged[i][j];
            }
        }

        // Correctness: every arm must reproduce the production encode exactly.
        byte[] expected = Encoding.ASCII.GetBytes(Base58.Bitcoin.Encode(_data));
        AssertMatches<ScalarMode>(expected);
        AssertMatches<SimdMode>(expected);
        AssertMatches<TensorMode>(expected);
    }

    private void AssertMatches<TMode>(byte[] expected)
        where TMode : struct, IMatmulMode
    {
        Span<byte> scratch = stackalloc byte[128];
        int len = EncodeFull<TMode>(_data, scratch);
        if (!scratch[..len].SequenceEqual(expected))
        {
            throw new InvalidOperationException($"EncodeFull<{typeof(TMode).Name}> does not match production Encode for size {Size}");
        }
    }

    // Reference arm: the current production scalar matmul over the jagged uint[][]. Left commented
    // so runs compare Simd vs Tensor only. Uncomment to include it; Simd stays the baseline (BDN
    // allows exactly one Baseline).
    //[Benchmark]
    //public int Scalar()
    //{
    //    Span<byte> dest = stackalloc byte[128];
    //    return EncodeFull<ScalarMode>(_data, dest);
    //}

    [Benchmark(Baseline = true)]
    public int Simd()
    {
        Span<byte> dest = stackalloc byte[128];
        return EncodeFull<SimdMode>(_data, dest);
    }

    [Benchmark]
    public int Tensor()
    {
        Span<byte> dest = stackalloc byte[128];
        return EncodeFull<TensorMode>(_data, dest);
    }

    private int EncodeFull<TMode>(ReadOnlySpan<byte> data, Span<byte> destination)
        where TMode : struct, IMatmulMode
    {
        return data.Length == 32
            ? EncodeFull32<TMode>(data, destination)
            : EncodeFull64<TMode>(data, destination);
    }

    [SkipLocalsInit]
    private int EncodeFull32<TMode>(ReadOnlySpan<byte> data, Span<byte> destination)
        where TMode : struct, IMatmulMode
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
        AccumulateRows<TMode>(intermediate, binary, 0, Base58BitcoinTables.BinarySz32, rowLen);

        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz32];
        return ReduceExtractEmit(intermediate, Base58BitcoinTables.IntermediateSz32, rawBase58, Base58BitcoinTables.Raw58Sz32, inLeadingZeros, destination);
    }

    [SkipLocalsInit]
    private int EncodeFull64<TMode>(ReadOnlySpan<byte> data, Span<byte> destination)
        where TMode : struct, IMatmulMode
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

        // Split matmul with an interleaved mini-reduction to keep the limbs from overflowing,
        // matching production ComputeBitcoin64FastRaw exactly.
        AccumulateRows<TMode>(intermediate, binary, 0, 8, rowLen);
        intermediate[15] += intermediate[16] / Base58BitcoinTables.R1Div;
        intermediate[16] %= Base58BitcoinTables.R1Div;
        AccumulateRows<TMode>(intermediate, binary, 8, Base58BitcoinTables.BinarySz64, rowLen);

        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz64];
        return ReduceExtractEmit(intermediate, Base58BitcoinTables.IntermediateSz64, rawBase58, Base58BitcoinTables.Raw58Sz64, inLeadingZeros, destination);
    }

    // The only step that differs between arms: intermediate[1..] += binary[i] * row[i] for each
    // row. TMode is a value type, so every `typeof(TMode) == typeof(...)` below is a compile-time
    // constant and only the matching block survives in each specialized instantiation.
    private void AccumulateRows<TMode>(
        Span<ulong> intermediate,
        ReadOnlySpan<uint> binary,
        int startRow,
        int endRow,
        int rowLen)
        where TMode : struct, IMatmulMode
    {
        if (typeof(TMode) == typeof(ScalarMode))
        {
            uint[][] jagged = _jagged;
            for (int i = startRow; i < endRow; i++)
            {
                uint[] row = jagged[i];
                for (int j = 0; j < rowLen; j++)
                {
                    intermediate[j + 1] += (ulong)binary[i] * row[j];
                }
            }

            return;
        }

        ulong[] rowMajor = _rowMajor;
        Span<ulong> target = intermediate.Slice(1, rowLen);

        if (typeof(TMode) == typeof(TensorMode))
        {
            for (int i = startRow; i < endRow; i++)
            {
                ReadOnlySpan<ulong> row = rowMajor.AsSpan(i * rowLen, rowLen);
                // destination[k] = (row[k] * binary[i]) + target[k]; addend and destination are the
                // same span (allowed: they begin at the same location).
                TensorPrimitives.MultiplyAdd(row, (ulong)binary[i], target, target);
            }

            return;
        }

        // SimdMode
        for (int i = startRow; i < endRow; i++)
        {
            ReadOnlySpan<ulong> row = rowMajor.AsSpan(i * rowLen, rowLen);
            MultiplyAddSimd(row, binary[i], target);
        }
    }

    // acc[k] += x[k] * scale, widest available width first, then a scalar tail / fallback.
    private static void MultiplyAddSimd(ReadOnlySpan<ulong> x, ulong scale, Span<ulong> acc)
    {
        ref ulong xr = ref MemoryMarshal.GetReference(x);
        ref ulong ar = ref MemoryMarshal.GetReference(acc);
        int len = x.Length;
        int i = 0;

        if (Vector256.IsHardwareAccelerated && len >= Vector256<ulong>.Count)
        {
            Vector256<ulong> s = Vector256.Create(scale);
            int upper = len - Vector256<ulong>.Count;
            for (; i <= upper; i += Vector256<ulong>.Count)
            {
                Vector256<ulong> a = Vector256.LoadUnsafe(ref ar, (nuint)i);
                Vector256<ulong> v = Vector256.LoadUnsafe(ref xr, (nuint)i);
                (a + (v * s)).StoreUnsafe(ref ar, (nuint)i);
            }
        }
        else if (Vector128.IsHardwareAccelerated && len >= Vector128<ulong>.Count)
        {
            Vector128<ulong> s = Vector128.Create(scale);
            int upper = len - Vector128<ulong>.Count;
            for (; i <= upper; i += Vector128<ulong>.Count)
            {
                Vector128<ulong> a = Vector128.LoadUnsafe(ref ar, (nuint)i);
                Vector128<ulong> v = Vector128.LoadUnsafe(ref xr, (nuint)i);
                (a + (v * s)).StoreUnsafe(ref ar, (nuint)i);
            }
        }

        for (; i < len; i++)
        {
            Unsafe.Add(ref ar, i) += Unsafe.Add(ref xr, i) * scale;
        }
    }

    private static int ReduceExtractEmit(
        Span<ulong> intermediate,
        int intermediateSz,
        Span<byte> rawBase58,
        int raw58Sz,
        int inLeadingZeros,
        Span<byte> destination)
    {
        // Reduce each limb to less than 58^5.
        for (int i = intermediateSz - 1; i > 0; i--)
        {
            intermediate[i - 1] += intermediate[i] / Base58BitcoinTables.R1Div;
            intermediate[i] %= Base58BitcoinTables.R1Div;
        }

        // Convert each limb to five base58 digits.
        for (int i = 0; i < intermediateSz; i++)
        {
            uint v = (uint)intermediate[i];
            rawBase58[5 * i + 4] = (byte)((v / 1U) % 58U);
            rawBase58[5 * i + 3] = (byte)((v / 58U) % 58U);
            rawBase58[5 * i + 2] = (byte)((v / 3364U) % 58U);
            rawBase58[5 * i + 1] = (byte)((v / 195112U) % 58U);
            rawBase58[5 * i + 0] = (byte)(v / 11316496U);
        }

        int rawLeadingZeros = 0;
        while (rawLeadingZeros < raw58Sz && rawBase58[rawLeadingZeros] == 0)
        {
            rawLeadingZeros++;
        }

        int digitCount = raw58Sz - rawLeadingZeros;
        int outputLength = inLeadingZeros + digitCount;

        if (inLeadingZeros > 0)
        {
            destination[..inLeadingZeros].Fill((byte)'1');
        }

        int index = inLeadingZeros;
        ReadOnlySpan<byte> alphabet = BitcoinAlphabet.Characters;
        for (int k = rawLeadingZeros; k < raw58Sz; k++)
        {
            destination[index++] = alphabet[rawBase58[k]];
        }

        return outputLength;
    }
}
