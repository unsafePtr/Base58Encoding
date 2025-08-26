using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using NBitcoin.DataEncoders;

namespace Base58Encoding.Benchmarks;

[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class Base58ComparisonBenchmark
{
    private byte[] _testData = null!;
    private string _ourBase58Encoded = null!;
    private string _simpleBase58Encoded = null!;

    [Params(8, 13, 32, 69)]
    public int DataSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _testData = new byte[DataSize];
        Random.Shared.NextBytes(_testData);

        // Add some leading zeros for realistic Base58 scenarios
        if (DataSize >= 2)
        {
            _testData[0] = 0;
        }
        if (DataSize > 32)
            _testData[1] = 0;

        _ourBase58Encoded = Base58.Bitcoin.Encode(_testData);
        _simpleBase58Encoded = Base58.Bitcoin.Encode(_testData.AsSpan());
    }

    [Benchmark(Description = "Our Base58 Encode")]
    public string Encode_OurBase58()
    {
        return Base58.Bitcoin.Encode(_testData);
    }

    [Benchmark(Description = "SimpleBase Base58 Encode")]
    public string Encode_SimpleBase58()
    {
        return SimpleBase.Base58.Bitcoin.Encode(_testData.AsSpan());
    }

    [Benchmark(Description = "NBitcoin Base58 Encode")]
    public string Encode_NBitcoin()
    {
        var bcEncoded = Encoders.Base58.EncodeData(_testData);
        return bcEncoded;
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