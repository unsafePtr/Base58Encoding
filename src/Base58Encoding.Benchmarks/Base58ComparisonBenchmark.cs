using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using Base58Encoding.Benchmarks.Common;

namespace Base58Encoding.Benchmarks;

[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[DisassemblyDiagnoser(exportCombinedDisassemblyReport: true)]
public class Base58ComparisonBenchmark
{
    private byte[] _testData = null!;
    private string _ourBase58Encoded = null!;
    private string _simpleBase58Encoded = null!;

    [Params(TestVectors.VectorType.BitcoinAddress,
        TestVectors.VectorType.SolanaAddress,
        TestVectors.VectorType.SolanaTx,
        TestVectors.VectorType.IPFSHash,
        TestVectors.VectorType.MoneroAddress,
        TestVectors.VectorType.FlickrTestData,
        TestVectors.VectorType.RippleTestData)]
    public TestVectors.VectorType VectorType { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _testData = TestVectors.GetVector(VectorType);

        _ourBase58Encoded = Base58.Bitcoin.Encode(_testData);
        _simpleBase58Encoded = SimpleBase.Base58.Bitcoin.Encode(_testData);
    }

    [Benchmark(Description = "Our Base58 Encode", Baseline = true)]
    public string Encode_OurBase58()
    {
        return Base58.Bitcoin.Encode(_testData);
    }

    [Benchmark(Description = "SimpleBase Base58 Encode")]
    public string Encode_SimpleBase58()
    {
        return SimpleBase.Base58.Bitcoin.Encode(_testData.AsSpan());
    }

    [Benchmark(Description = "Our Base58 Decode", Baseline = true)]
    public byte[] Decode_OurBase58()
    {
        return Base58.Bitcoin.Decode(_ourBase58Encoded);
    }

    [Benchmark(Description = "SimpleBase Base58 Decode")]
    public byte[] Decode_SimpleBase58()
    {
        return SimpleBase.Base58.Bitcoin.Decode(_simpleBase58Encoded.AsSpan());
    }
}