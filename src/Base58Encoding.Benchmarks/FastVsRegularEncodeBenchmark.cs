using BenchmarkDotNet.Attributes;

namespace Base58Encoding.Benchmarks;

[MemoryDiagnoser]
public class FastVsRegularEncodeBenchmark
{
    public byte[] _data;
    private string _encodedBase58;

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[32];

        Random.Shared.NextBytes(_data);

        _encodedBase58 = SimpleBase.Base58.Bitcoin.Encode(_data);
    }

    [Benchmark]
    public string RegularEncode()
    {
        return Base58.Bitcoin.EncodeGeneric(_data);
    }

    [Benchmark]
    public string FastEncode()
    {
        return Base58.EncodeBitcoin32Fast(_data);
    }


    [Benchmark]
    public byte[] RegularDecode()
    {
        return Base58.Bitcoin.DecodeGeneric(_encodedBase58);
    }

    [Benchmark]
    public byte[] FastDecode()
    {
        return Base58.DecodeBitcoin32Fast(_encodedBase58)!;
    }
}
