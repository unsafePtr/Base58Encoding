namespace Base58Encoding;

public static partial class Base58
{
    public static Base58<BitcoinAlphabet> Bitcoin { get; } = new();
    public static Base58<RippleAlphabet> Ripple { get; } = new();
    public static Base58<FlickrAlphabet> Flickr { get; } = new();

    internal static string EncodeBitcoin32Fast(ReadOnlySpan<byte> data)
        => Base58<BitcoinAlphabet>.EncodeBitcoin32FastToString(data);

    internal static string EncodeBitcoin64Fast(ReadOnlySpan<byte> data)
        => Base58<BitcoinAlphabet>.EncodeBitcoin64FastToString(data);

    internal static byte[]? DecodeBitcoin32Fast(ReadOnlySpan<char> encoded)
        => Base58<BitcoinAlphabet>.DecodeBitcoin32Fast(encoded);

    internal static byte[]? DecodeBitcoin64Fast(ReadOnlySpan<char> encoded)
        => Base58<BitcoinAlphabet>.DecodeBitcoin64Fast(encoded);
}
