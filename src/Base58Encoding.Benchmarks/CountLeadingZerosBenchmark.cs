using BenchmarkDotNet.Attributes;
using System.Runtime.CompilerServices;

namespace Base58Encoding.Benchmarks;

public class CountLeadingZerosBenchmark
{
    private static Lazy<byte[][]> _lazyTestData = new(() =>
    {
        // Decode the provided base58 addresses to get byte arrays with leading zeros
        var addresses = new[]
        {
            "1A1zP12r2r2r1wq1",
            "1111111111111111111114oLvT2",
            "1111113LrYkRba5STgvA5UoLqzLxwP6XhtN6f",
            "1BoatSLRHtKNngkdXEeobR76b53LETtpyT",
            "11bJr6xq2UhxDqkdqNKoGPYYEVBy6cd3M"
        };
        var data = new byte[addresses.Length][];
        for (int i = 0; i < addresses.Length; i++)
        {
            data[i] = Base58.Bitcoin.Decode(addresses[i]);
        }
        return data;
    });

    public byte[][] TestData;


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
            totalCount += Base58.CountLeadingZerosScalar(data);
        }
        return totalCount;
    }

    [Benchmark(Description = "SIMD+Scalar")]
    public int SimdOnly()
    {
        int totalCount = 0;
        foreach (var data in TestData)
        {
            int count = Base58.CountLeadingZerosSimd(data, out int processed);
            if (count < processed)
                return count;

            var total = count + Base58.CountLeadingZerosScalar(data.AsSpan(count));

            totalCount += total;
        }

        return totalCount;
    }

    [Benchmark(Baseline = true, Description = "Combined (Current)")]
    public int Combined()
    {
        int totalCount = 0;
        foreach (var data in TestData)
        {
            totalCount += Base58.CountLeadingZeros(data);
        }
        return totalCount;
    }

    // Simple array-based approach
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
}