using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Jobs;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Base58Encoding.Benchmarks;

[SimpleJob(RuntimeMoniker.Net90)]
[SimpleJob(RuntimeMoniker.Net10_0)]
[HideColumns("Job", "Error", "StdDev", "Median", "RatioSD")]
[DisassemblyDiagnoser]
public class CountLeadingZerosBenchmark
{
    private static Lazy<byte[][]> _lazyTestData = new(() =>
    {
        var testCases = new List<byte[]>();

        // Bitcoin addresses with varying leading zeros
        testCases.Add(Base58.Bitcoin.Decode("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa")); // 0 leading zeros (standard P2PKH)
        testCases.Add(Base58.Bitcoin.Decode("1111111111111111111114oLvT2")); // Many leading zeros
        testCases.Add(Base58.Bitcoin.Decode("1BoatSLRHtKNngkdXEeobR76b53LETtpyT")); // 0 leading zeros (standard)

        // Solana addresses (32 bytes) - realistic cases
        testCases.Add(Base58.Bitcoin.Decode("11111111111111111111111111111112")); // 31 leading zeros (system program)
        testCases.Add(Base58.Bitcoin.Decode("1111111QLbz7JHiBTspS962RLKV8GndWFwiEaqKM")); // 1 leading zero
        testCases.Add(Base58.Bitcoin.Decode("4TMFNY21gmHLT3HpPZDiXY6kQkGPpJsJRfisJ9T7rXdV")); // Typical Solana address (0 leading zeros)
        testCases.Add(Base58.Bitcoin.Decode("So11111111111111111111111111111111111111112")); // Wrapped SOL (0 leading zeros)

        // IPFS hashes (46 bytes) - typical CIDv0
        testCases.Add(Base58.Bitcoin.Decode("QmYwAPJzv5CZsnA625s3Xf2nemtYgPpHdWEz79ojWnPbdG")); // No leading zeros (typical)

        // Sui transaction digests (32 bytes)
        testCases.Add(Base58.Bitcoin.Decode("11111118YbNzKyoNPP4TJLYwpB2B3NCvpNXJbRNcszU")); // Many leading zeros
        testCases.Add(Base58.Bitcoin.Decode("3aDawfe6rm6QnFhJGppyyHVxXfEhBGYPMD8nzfLKNJqq"));

        // Typical blockchain transaction hashes
        testCases.Add(Base58.Bitcoin.Decode("4vJ9JU1bJJE96FWSJKvHsmmFADCg4gpZQff4P3bkLKi")); // 0 leading zeros
        testCases.Add(Base58.Bitcoin.Decode("11BxRVsYgGSUZRSMaNcNjSzwMBYXqZQvBQr3BQznBFmpV")); // 2 leading zeros

        return testCases.ToArray();
    });

    private byte[][] TestData = default!;

    [GlobalSetup]
    public void Setup()
    {
        TestData = _lazyTestData.Value;
    }

    [Benchmark(Description = "Simple Array")]
    public int SimpleArray()
    {
        int totalCount = 0;
        foreach (var data in TestData)
        {
            totalCount += CountLeadingZerosArray(data);
        }
        return totalCount;
    }

    [Benchmark(Description = "Scalar (Current)")]
    public int Scalar()
    {
        int totalCount = 0;
        foreach (var data in TestData)
        {
            totalCount += CountLeadingZerosScalarImpl(data);
        }
        return totalCount;
    }

    [Benchmark(Description = "SIMD+Scalar")]
    public int SimdOnly()
    {
        int totalCount = 0;
        foreach (var data in TestData)
        {
            int count = CountLeadingZerosSimdImpl(data, out int processed);
            if (count < processed)
            {
                totalCount += count;
            }
            else
            {
                totalCount += count + CountLeadingZerosScalarImpl(data.AsSpan(count));
            }
        }

        return totalCount;
    }

    [Benchmark(Baseline = true, Description = "Combined (Current) - 16bytes SIMD threshold")]
    public int Combined()
    {
        int totalCount = 0;
        foreach (var data in TestData)
        {
            totalCount += CountLeadingZerosCombinedImpl(data);
        }
        return totalCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountLeadingZerosArray(ReadOnlySpan<byte> data)
    {
        int count = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != 0)
                break;
            count++;
        }
        return count;
    }

    // Copy of Base58.CountLeadingZerosScalar implementation
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountLeadingZerosScalarImpl(ReadOnlySpan<byte> data)
    {
        int count = 0;
        int length = data.Length;
        ref byte searchSpace = ref MemoryMarshal.GetReference(data);

        while (length >= sizeof(ulong))
        {
            ulong value = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref searchSpace, count));
            if (value != 0)
            {
                if (BitConverter.IsLittleEndian)
                {
                    count += BitOperations.TrailingZeroCount(value) / 8;
                }
                else
                {
                    count += BitOperations.LeadingZeroCount(value) / 8;
                }
                return count;
            }
            count += sizeof(ulong);
            length -= sizeof(ulong);
        }

        while (count < data.Length && data[count] == 0)
        {
            count++;
        }

        return count;
    }

    // Copy of Base58.CountLeadingZerosSimd implementation
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountLeadingZerosSimdImpl(ReadOnlySpan<byte> data, out int processed)
    {
        int count = 0;
        int length = data.Length;
        ref byte searchSpace = ref MemoryMarshal.GetReference(data);

        if (Vector256.IsHardwareAccelerated && length >= Vector256<byte>.Count)
        {
            var zeroVector = Vector256<byte>.Zero;

            while (length >= Vector256<byte>.Count)
            {
                var vector = Vector256.LoadUnsafe(ref searchSpace, (nuint)count);
                var comparison = Vector256.Equals(vector, zeroVector);
                uint mask = comparison.ExtractMostSignificantBits();

                if (mask != uint.MaxValue)
                {
                    processed = count + Vector256<byte>.Count;
                    return count + BitOperations.TrailingZeroCount(~mask);
                }

                count += Vector256<byte>.Count;
                length -= Vector256<byte>.Count;
            }
        }
        else if (Vector128.IsHardwareAccelerated && length >= Vector128<byte>.Count)
        {
            var zeroVector = Vector128<byte>.Zero;

            while (length >= Vector128<byte>.Count)
            {
                var vector = Vector128.LoadUnsafe(ref searchSpace, (nuint)count);
                var comparison = Vector128.Equals(vector, zeroVector);
                uint mask = comparison.ExtractMostSignificantBits();

                if (mask != ushort.MaxValue)
                {
                    processed = count + Vector128<byte>.Count;
                    return count + BitOperations.TrailingZeroCount(~mask);
                }

                count += Vector128<byte>.Count;
                length -= Vector128<byte>.Count;
            }
        }

        processed = count;
        return count;
    }

    // Copy of Base58.CountLeadingZeros implementation
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountLeadingZerosCombinedImpl(ReadOnlySpan<byte> data)
    {
        if (data.Length < 64)
            return CountLeadingZerosScalarImpl(data);

        int count = CountLeadingZerosSimdImpl(data, out int processed);
        if (count < processed)
            return count;

        return count + CountLeadingZerosScalarImpl(data.Slice(count));
    }
}