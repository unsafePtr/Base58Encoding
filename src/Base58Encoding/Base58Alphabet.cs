namespace Base58Encoding;

public class Base58Alphabet
{
    public const string BitcoinAlphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    public const string RippleAlphabet = "rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz";
    public const string FlickrAlphabet = "123456789abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";

    public readonly ReadOnlyMemory<char> Characters;
    public readonly ReadOnlyMemory<byte> DecodeTable;
    public readonly char FirstCharacter;

    private Base58Alphabet(ReadOnlyMemory<char> characters, ReadOnlyMemory<byte> decodeTable, char firstCharacter)
    {
        Characters = characters;
        DecodeTable = decodeTable;
        FirstCharacter = firstCharacter;
    }

    // Cached static instances for common alphabets
    private static readonly Lazy<Base58Alphabet> _bitcoin = new(() => new(
        BitcoinAlphabet.AsMemory(),
        BitcoinDecodeTable,
        '1'
    ));

    private static readonly Lazy<Base58Alphabet> _ripple = new(() => new(
        RippleAlphabet.AsMemory(),
        RippleDecodeTable,
        'r'
    ));

    private static readonly Lazy<Base58Alphabet> _flickr = new(() => new(
        FlickrAlphabet.AsMemory(),
        FlickrDecodeTable,
        '1'
    ));

    public static Base58Alphabet Bitcoin => _bitcoin.Value;
    public static Base58Alphabet Ripple => _ripple.Value;
    public static Base58Alphabet Flickr => _flickr.Value;

    // Static decode tables - using ReadOnlyMemory for better performance
    private static readonly ReadOnlyMemory<byte> BitcoinDecodeTable = new byte[]
    {
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255,   0,   1,   2,   3,   4,   5,   6,   7,   8, 255, 255, 255, 255, 255, 255,
        255,   9,  10,  11,  12,  13,  14,  15,  16, 255,  17,  18,  19,  20,  21, 255,
         22,  23,  24,  25,  26,  27,  28,  29,  30,  31,  32, 255, 255, 255, 255, 255,
        255,  33,  34,  35,  36,  37,  38,  39,  40,  41,  42,  43, 255,  44,  45,  46,
         47,  48,  49,  50,  51,  52,  53,  54,  55,  56,  57, 255, 255, 255, 255, 255
    };

    private static readonly ReadOnlyMemory<byte> RippleDecodeTable = new byte[]
    {
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255,  50,  33,   7,  21,  41,  40,  27,  45,   8, 255, 255, 255, 255, 255, 255,
        255,  54,  10,  38,  12,  14,  47,  15,  16, 255,  17,  18,  19,  20,  13, 255,
         22,  23,  24,  25,  26,  11,  28,  29,  30,  31,  32, 255, 255, 255, 255, 255,
        255,   5,  34,  35,  36,  37,   6,  39,   3,  49,  42,  43, 255,  44,   4,  46,
          1,  48,   0,   2,  51,  52,  53,   9,  55,  56,  57, 255, 255, 255, 255, 255
    };

    private static readonly ReadOnlyMemory<byte> FlickrDecodeTable = new byte[]
    {
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255,   0,   1,   2,   3,   4,   5,   6,   7,   8, 255, 255, 255, 255, 255, 255,
        255,  34,  35,  36,  37,  38,  39,  40,  41, 255,  42,  43,  44,  45,  46, 255,
         47,  48,  49,  50,  51,  52,  53,  54,  55,  56,  57, 255, 255, 255, 255, 255,
        255,   9,  10,  11,  12,  13,  14,  15,  16,  17,  18,  19, 255,  20,  21,  22,
         23,  24,  25,  26,  27,  28,  29,  30,  31,  32,  33, 255, 255, 255, 255, 255
    };
}
