namespace Base58Encoding;

public interface IBase58Alphabet
{
    static abstract ReadOnlySpan<byte> Characters { get; }
    static abstract ReadOnlySpan<byte> DecodeTable { get; }
    static abstract byte FirstCharacter { get; }
}
