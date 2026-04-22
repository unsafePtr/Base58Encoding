namespace Base58Encoding;

public interface IBase58Alphabet
{
    static abstract ReadOnlySpan<char> Characters { get; }
    static abstract ReadOnlySpan<byte> DecodeTable { get; }
    static abstract char FirstCharacter { get; }
}
