using Base58Encoding.Benchmarks.Common;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;

namespace Base58Encoding.Benchmarks;

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[HideColumns("Job", "Error", "StdDev", "Median", "RatioSD")]
public class Base58ComparisonBenchmark
{
    private byte[] _testData = null!;
    private string _base58Encoded = null!;

    [Params(TestVectors.VectorType.BitcoinAddress,
        TestVectors.VectorType.SolanaAddress,
        TestVectors.VectorType.SolanaTx,
        TestVectors.VectorType.IPFSHash,
        TestVectors.VectorType.MoneroAddress
    )]
    public TestVectors.VectorType VectorType { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _testData = TestVectors.GetVector(VectorType);

        _base58Encoded = Base58.Bitcoin.Encode(_testData);
    }

    [BenchmarkCategory("Encode"), Benchmark(Description = "Our Base58 Encode", Baseline = true)]
    public string Encode_OurBase58()
    {
        return Base58.Bitcoin.Encode(_testData);
    }

    [BenchmarkCategory("Encode"), Benchmark(Description = "SimpleBase Base58 Encode")]
    public string Encode_SimpleBase58()
    {
        return SimpleBase.Base58.Bitcoin.Encode(_testData);
    }

    [BenchmarkCategory("Decode"), Benchmark(Description = "Our Base58 Decode", Baseline = true)]
    public byte[] Decode_OurBase58()
    {
        return Base58.Bitcoin.Decode(_base58Encoded);
    }

    [BenchmarkCategory("Decode"), Benchmark(Description = "SimpleBase Base58 Decode")]
    public byte[] Decode_SimpleBase58()
    {
        return SimpleBase.Base58.Bitcoin.Decode(_base58Encoded);
    }
}
