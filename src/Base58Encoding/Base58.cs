namespace Base58Encoding;

public partial class Base58
{
    private const int Base = 58;
    private const int MaxStackallocByte = 512;

    private readonly ReadOnlyMemory<char> _characters;
    private readonly ReadOnlyMemory<byte> _decodeTable;
    private readonly char _firstCharacter;

    private static readonly Lazy<Base58> _bitcoin = new(() => new(Base58Alphabet.Bitcoin));
    private static readonly Lazy<Base58> _ripple = new(() => new(Base58Alphabet.Ripple));
    private static readonly Lazy<Base58> _flickr = new(() => new(Base58Alphabet.Flickr));

    public static Base58 Bitcoin => _bitcoin.Value;
    public static Base58 Ripple => _ripple.Value;
    public static Base58 Flickr => _flickr.Value;

    private Base58(Base58Alphabet alphabet)
    {
        _characters = alphabet.Characters;
        _decodeTable = alphabet.DecodeTable;
        _firstCharacter = alphabet.FirstCharacter;
    }
}
