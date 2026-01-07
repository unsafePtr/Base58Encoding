using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Base58Encoding.Benchmarks.Common;

namespace Base58Encoding.Benchmarks;

[MemoryDiagnoser]
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

    [Benchmark(Description = "Our Base58 Encode", Baseline = true)]
    public string Encode_OurBase58()
    {
        return Base58.Bitcoin.Encode(_testData);
    }

    [Benchmark(Description = "SimpleBase Base58 Encode")]
    public string Encode_SimpleBase58()
    {
        return SimpleBase.Base58.Bitcoin.Encode(_testData);
    }

    [Benchmark(Description = "Our Base58 Decode")]
    public byte[] Decode_OurBase58()
    {
        return Base58.Bitcoin.Decode(_base58Encoded);
    }

    [Benchmark(Description = "SimpleBase Base58 Decode")]
    public byte[] Decode_SimpleBase58()
    {
        return SimpleBase.Base58.Bitcoin.Decode(_base58Encoded);
    }
}