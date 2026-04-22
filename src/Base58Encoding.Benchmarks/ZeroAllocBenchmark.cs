using Base58Encoding.Benchmarks.Common;

using BenchmarkDotNet.Attributes;

namespace Base58Encoding.Benchmarks;

[MemoryDiagnoser(false)]
[HideColumns("Job", "Error", "StdDev", "Median", "RatioSD")]
public class ZeroAllocBenchmark
{
    private byte[] _testData = null!;
    private byte[] _encodedBytes = null!;
    private byte[] _encodeDest = null!;
    private byte[] _decodeDest = null!;

    [Params(
        TestVectors.VectorType.BitcoinAddress,
        TestVectors.VectorType.SolanaAddress,
        TestVectors.VectorType.SolanaTx,
        TestVectors.VectorType.MoneroAddress
    )]
    public TestVectors.VectorType VectorType { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _testData = TestVectors.GetVector(VectorType);
        _encodeDest = new byte[Base58.GetMaxEncodedLength(_testData.Length)];
        int written = Base58.Bitcoin.Encode(_testData, _encodeDest);
        _encodedBytes = _encodeDest[..written];
        _decodeDest = new byte[_testData.Length];
    }

    [Benchmark]
    public int Encode() => Base58.Bitcoin.Encode(_testData, _encodeDest);

    [Benchmark]
    public int Decode() => Base58.Bitcoin.Decode(_encodedBytes, _decodeDest);
}
