namespace Base58Encoding.Benchmarks.Common;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2386:Mutable fields should not be \"public static\"", Justification = "For benchmarking it's fine")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Bug", "S3887:Mutable, non-private fields should not be \"readonly\"", Justification = "For benchmarking it's fine")]
public static class TestVectors
{
    public static readonly byte[] BitcoinAddress = Base58.Bitcoin.Decode("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa"); // First Bitcoin address (25 bytes)
    public static readonly byte[] SolanaAddress = Base58.Bitcoin.Decode("7CQ3QzpcRjsxzRJugSdFUrAVoSSnpR7pyAFhbNCaZBPs"); // Solana wallet (32 bytes)
    public static readonly byte[] SolanaTx = Base58.Bitcoin.Decode("5VERv8NMvzbJMEkV8xnrLkEaWRtSz9CosKDYjCJjBRnbJLgp8uirBgmQpjKhoR4tjF3ZpRzrFmBV6UjKdiSZkQUW"); // Solana TX signature (64 bytes)
    public static readonly byte[] IPFSHash = Base58.Bitcoin.Decode("QmYwAPJzv5CZsnA625s3Xf2nemtYgPpHdWEz79ojWnPbdG"); // IPFS hash (34 bytes)
    public static readonly byte[] MoneroAddress = Base58.Bitcoin.Decode("4AdUndXHHZ6cfufTMvppY6JwXNouMBzSkbLYfpAV5Usx3skxNgYeYTRj5UzqtReoS44qo9mtmXCqY45DJ852K5Jv2684Rge"); // Monero address 69 bytes

    // Additional test vectors for Flickr/Ripple alphabets
    public static readonly byte[] FlickrTestData = Base58.Flickr.Decode("5Kd3NBUAdUnhyzenEwVLy9pBKxSwXvE9FMPyR4UKZvpe6E3CmWo"); // Flickr encoded data (37 bytes)
    public static readonly byte[] RippleTestData = Base58.Ripple.Decode("shVsJ6ZSWEuNpWxuRYAfWsC9PjUsEupGJB8QbF7DJCRLyNyy"); // Ripple encoded data (32 bytes)

    public enum VectorType
    {
        BitcoinAddress,
        SolanaAddress,
        SolanaTx,
        IPFSHash,
        MoneroAddress,
        FlickrTestData,
        RippleTestData
    }

    public static byte[] GetVector(VectorType type)
    {
        return type switch
        {
            VectorType.BitcoinAddress => BitcoinAddress,
            VectorType.SolanaAddress => SolanaAddress,
            VectorType.SolanaTx => SolanaTx,
            VectorType.IPFSHash => IPFSHash,
            VectorType.MoneroAddress => MoneroAddress,
            VectorType.FlickrTestData => FlickrTestData,
            VectorType.RippleTestData => RippleTestData,
            _ => BitcoinAddress
        };
    }
}
