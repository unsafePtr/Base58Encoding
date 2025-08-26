using System.Runtime.CompilerServices;

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

    public Base58(Base58Alphabet alphabet)
    {
        _characters = alphabet.Characters;
        _decodeTable = alphabet.DecodeTable;
        _firstCharacter = alphabet.FirstCharacter;
    }

    /// <summary>
    /// Encode byte array to Base58 string
    /// </summary>
    /// <param name="data">Bytes to encode</param>
    /// <returns>Base58 encoded string</returns>
    public string Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return string.Empty;

        int leadingZeros = CountLeadingZeros(data);

        if (leadingZeros == data.Length)
        {
            return new string(_firstCharacter, leadingZeros);
        }

        var inputSpan = data[leadingZeros..];

        var size = (inputSpan.Length * 137 / 100) + 1;
        Span<byte> digits = size > MaxStackallocByte
            ? new byte[size]
            : stackalloc byte[size];

        int digitCount = 1;
        digits[0] = 0;

        foreach (byte b in inputSpan)
        {
            int carry = b;

            for (int i = 0; i < digitCount; i++)
            {
                carry += digits[i] << 8;
                carry = Math.DivRem(carry, Base, out int remainder);
                digits[i] = (byte)remainder;
            }

            while (carry > 0)
            {
                carry = Math.DivRem(carry, Base, out int remainder);
                digits[digitCount++] = (byte)remainder;
            }
        }

        int resultSize = leadingZeros + digitCount;
        return string.Create(resultSize, new FinalString(_characters.Span, digits, _firstCharacter, leadingZeros, digitCount), static (span, state) =>
        {
            if (state.LeadingZeroes > 0)
            {
                span[..state.LeadingZeroes].Fill(state.FirstCharacter);
            }

            int index = state.LeadingZeroes;
            for (int i = state.DigitCount - 1; i >= 0; i--)
            {
                span[index++] = state.Alphabet[state.Digits[i]];
            }
        });
    }

    /// <summary>
    /// Decode Base58 string to byte array
    /// </summary>
    /// <param name="encoded">Base58 encoded string</param>
    /// <returns>Decoded byte array</returns>
    /// <exception cref="ArgumentException">Invalid Base58 character</exception>
    public byte[] Decode(ReadOnlySpan<char> encoded)
    {
        if (encoded.IsEmpty)
            return [];

        int leadingOnes = CountLeadingCharacters(encoded, _firstCharacter);

        int outputSize = encoded.Length * 733 / 1000 + 1;

        Span<byte> decoded = outputSize > MaxStackallocByte
            ? new byte[outputSize]
            : stackalloc byte[outputSize];

        int decodedLength = 1;
        decoded[0] = 0;

        var decodeTable = _decodeTable.Span;

        for (int i = leadingOnes; i < encoded.Length; i++)
        {
            char c = encoded[i];

            if (c >= 128 || decodeTable[c] == 255)
                ThrowHelper.ThrowInvalidCharacter(c);

            int carry = decodeTable[c];

            for (int j = 0; j < decodedLength; j++)
            {
                carry += decoded[j] * Base;
                decoded[j] = (byte)(carry & 0xFF);
                carry >>= 8;
            }

            while (carry > 0)
            {
                decoded[decodedLength++] = (byte)(carry & 0xFF);
                carry >>= 8;
            }
        }

        var result = new byte[leadingOnes + decodedLength];

        var finalDecoded = decoded.Slice(0, decodedLength);
        finalDecoded.Reverse();

        finalDecoded.CopyTo(result.AsSpan(leadingOnes));

        return result;
    }

    private readonly ref struct FinalString
    {
        public readonly ReadOnlySpan<char> Alphabet;
        public readonly ReadOnlySpan<byte> Digits;
        public readonly char FirstCharacter;
        public readonly int LeadingZeroes;
        public readonly int DigitCount;

        public FinalString(ReadOnlySpan<char> alphabet, ReadOnlySpan<byte> digits, char firstCharacter, int leadingZeroes, int digitCount)
        {
            Alphabet = alphabet;
            Digits = digits;
            FirstCharacter = firstCharacter;
            LeadingZeroes = leadingZeroes;
            DigitCount = digitCount;
        }
    }
}