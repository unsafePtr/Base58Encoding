using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Base58Encoding.Benchmarks;

// End-to-end 64-byte decode A/B. Both arms run the identical full decode path (validate,
// intermediate build, matmul, carry-reduce, leading-zero check, byte output) and differ ONLY
// in the matmul step (SIMD TensorDot over transposed ulong columns vs jagged scalar). Measured
// in a single run, so the ratio is the real end-to-end effect of the matmul change.
[SimpleJob(RuntimeMoniker.Net10_0)]
[HideColumns("Error", "StdDev", "RatioSD")]
public class EndToEndDecodeBenchmark
{
    private string _sig64 = default!;
    private ulong[] _transposed64 = default!;
    private uint[][] _jagged64 = default!;

    [GlobalSetup]
    public void Setup()
    {
        var bytes = new byte[64];
        Random.Shared.NextBytes(bytes);
        _sig64 = Base58.Bitcoin.Encode(bytes);

        // Production tables on this branch are transposed column-major ulong.
        _transposed64 = Base58BitcoinTables.DecodeTable64;

        // Rebuild the original jagged row-major layout: jagged[i][j] = transposed[j*IntermediateSz64 + i].
        int rows = Base58BitcoinTables.IntermediateSz64;
        int cols = Base58BitcoinTables.BinarySz64;
        _jagged64 = new uint[rows][];
        for (int i = 0; i < rows; i++)
        {
            _jagged64[i] = new uint[cols];
            for (int j = 0; j < cols; j++)
            {
                _jagged64[i][j] = (uint)_transposed64[j * rows + i];
            }
        }

        // Correctness: both matmul variants must reproduce the production decode exactly.
        Span<byte> a = stackalloc byte[64];
        Span<byte> b = stackalloc byte[64];
        DecodeFull64(_sig64, a, useSimd: true);
        DecodeFull64(_sig64, b, useSimd: false);
        byte[] expected = Base58.Bitcoin.Decode(_sig64);
        if (!a.SequenceEqual(expected) || !b.SequenceEqual(expected))
        {
            throw new InvalidOperationException("DecodeFull64 does not match production Decode");
        }
    }

    [Benchmark(Baseline = true)]
    public int Full_Jagged()
    {
        Span<byte> dest = stackalloc byte[64];
        return DecodeFull64(_sig64, dest, useSimd: false);
    }

    [Benchmark]
    public int Full_Simd()
    {
        Span<byte> dest = stackalloc byte[64];
        return DecodeFull64(_sig64, dest, useSimd: true);
    }

    [SkipLocalsInit]
    private int DecodeFull64(ReadOnlySpan<char> encoded, Span<byte> destination, bool useSimd)
    {
        int charCount = encoded.Length;

        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz64];
        ReadOnlySpan<byte> decodeTable = BitcoinAlphabet.DecodeTable;

        int prepend0 = Base58BitcoinTables.Raw58Sz64 - charCount;
        for (int j = 0; j < Base58BitcoinTables.Raw58Sz64; j++)
        {
            if (j < prepend0)
            {
                rawBase58[j] = 0;
            }
            else
            {
                int c = encoded[j - prepend0];
                if ((uint)c >= 128 || decodeTable[c] == 255)
                {
                    ThrowHelper.ThrowInvalidCharacter((char)c);
                }

                rawBase58[j] = decodeTable[c];
            }
        }

        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz64];
        for (int i = 0; i < Base58BitcoinTables.IntermediateSz64; i++)
        {
            intermediate[i] = (ulong)rawBase58[5 * i + 0] * 11316496UL +
                              (ulong)rawBase58[5 * i + 1] * 195112UL +
                              (ulong)rawBase58[5 * i + 2] * 3364UL +
                              (ulong)rawBase58[5 * i + 3] * 58UL +
                              (ulong)rawBase58[5 * i + 4] * 1UL;
        }

        Span<ulong> binary = stackalloc ulong[Base58BitcoinTables.BinarySz64];
        if (useSimd)
        {
            var table = _transposed64;
            int rows = Base58BitcoinTables.IntermediateSz64;
            for (int j = 0; j < Base58BitcoinTables.BinarySz64; j++)
            {
                binary[j] = TensorDot(intermediate, table.AsSpan(j * rows, rows));
            }
        }
        else
        {
            var table = _jagged64;
            for (int j = 0; j < Base58BitcoinTables.BinarySz64; j++)
            {
                ulong acc = 0UL;
                for (int i = 0; i < Base58BitcoinTables.IntermediateSz64; i++)
                {
                    acc += intermediate[i] * table[i][j];
                }

                binary[j] = acc;
            }
        }

        for (int i = Base58BitcoinTables.BinarySz64 - 1; i > 0; i--)
        {
            binary[i - 1] += binary[i] >> 32;
            binary[i] &= 0xFFFFFFFFUL;
        }

        if (binary[0] > 0xFFFFFFFFUL)
        {
            return -1;
        }

        int outputLeadingZeros = 0;
        for (int i = 0; i < Base58BitcoinTables.BinarySz64; i++)
        {
            uint v = (uint)binary[i];
            if (v != 0)
            {
                outputLeadingZeros += BitOperations.LeadingZeroCount(v) / 8;
                break;
            }
            outputLeadingZeros += 4;
        }

        int inputLeadingOnes = Base58.CountLeadingCharacters(encoded, '1');
        if (outputLeadingZeros != inputLeadingOnes)
        {
            return -1;
        }

        for (int i = 0; i < Base58BitcoinTables.BinarySz64; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(i * sizeof(uint), sizeof(uint)), (uint)binary[i]);
        }

        return 64;
    }

    private static ulong TensorDot(ReadOnlySpan<ulong> x, ReadOnlySpan<ulong> y)
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
}
