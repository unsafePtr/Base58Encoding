namespace Base58Encoding;

public sealed partial class Base58<TAlphabet>
    where TAlphabet : struct, IBase58Alphabet
{
    private const int Base = 58;
    private const int MaxStackallocByte = 256;

    internal string EncodeGeneric(ReadOnlySpan<byte> data)
        => data.IsEmpty ? string.Empty : EncodeGenericToString(data);

    internal byte[] DecodeGeneric(ReadOnlySpan<char> encoded)
    {
        if (encoded.IsEmpty)
        {
            return [];
        }

        return DecodeGenericToArray<char>(encoded);
    }
}
