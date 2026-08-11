namespace Base58Encoding;

public sealed partial class Base58<TAlphabet>
    where TAlphabet : struct, IBase58Alphabet
{
    private const int Base = 58;

    // The generic encode path carries the value in base 58^5, the largest power of 58 below 2^32,
    // so one uint limb expands to exactly five base-58 digits.
    private const uint Base58Pow5 = Base58BitcoinTables.R1Div;
    private const int Base58Pow5Digits = 5;

    // Ceiling on the stackalloc'd raw digit buffer. The generic encode path also stackallocs one
    // uint limb per five digits alongside it, so the real stack ceiling is 1.8x this.
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
