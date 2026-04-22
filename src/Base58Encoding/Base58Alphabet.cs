namespace Base58Encoding;

public readonly struct BitcoinAlphabet : IBase58Alphabet
{
    public static ReadOnlySpan<byte> Characters => "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz"u8;
    public static byte FirstCharacter => (byte)'1';
    public static ReadOnlySpan<byte> DecodeTable =>
    [
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255,   0,   1,   2,   3,   4,   5,   6,   7,   8, 255, 255, 255, 255, 255, 255,
        255,   9,  10,  11,  12,  13,  14,  15,  16, 255,  17,  18,  19,  20,  21, 255,
         22,  23,  24,  25,  26,  27,  28,  29,  30,  31,  32, 255, 255, 255, 255, 255,
        255,  33,  34,  35,  36,  37,  38,  39,  40,  41,  42,  43, 255,  44,  45,  46,
         47,  48,  49,  50,  51,  52,  53,  54,  55,  56,  57, 255, 255, 255, 255, 255,
    ];
}

public readonly struct RippleAlphabet : IBase58Alphabet
{
    public static ReadOnlySpan<byte> Characters => "rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz"u8;
    public static byte FirstCharacter => (byte)'r';
    public static ReadOnlySpan<byte> DecodeTable =>
    [
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255,  50,  33,   7,  21,  41,  40,  27,  45,   8, 255, 255, 255, 255, 255, 255,
        255,  54,  10,  38,  12,  14,  47,  15,  16, 255,  17,  18,  19,  20,  13, 255,
         22,  23,  24,  25,  26,  11,  28,  29,  30,  31,  32, 255, 255, 255, 255, 255,
        255,   5,  34,  35,  36,  37,   6,  39,   3,  49,  42,  43, 255,  44,   4,  46,
          1,  48,   0,   2,  51,  52,  53,   9,  55,  56,  57, 255, 255, 255, 255, 255,
    ];
}

public readonly struct FlickrAlphabet : IBase58Alphabet
{
    public static ReadOnlySpan<byte> Characters => "123456789abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ"u8;
    public static byte FirstCharacter => (byte)'1';
    public static ReadOnlySpan<byte> DecodeTable =>
    [
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255,   0,   1,   2,   3,   4,   5,   6,   7,   8, 255, 255, 255, 255, 255, 255,
        255,  34,  35,  36,  37,  38,  39,  40,  41, 255,  42,  43,  44,  45,  46, 255,
         47,  48,  49,  50,  51,  52,  53,  54,  55,  56,  57, 255, 255, 255, 255, 255,
        255,   9,  10,  11,  12,  13,  14,  15,  16,  17,  18,  19, 255,  20,  21,  22,
         23,  24,  25,  26,  27,  28,  29,  30,  31,  32,  33, 255, 255, 255, 255, 255,
    ];
}
