using System.Buffers.Binary;

namespace Base58Encoding;

public partial class Base58
{
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

        // Hot path for Bitcoin alphabet + common expected output sizes
        if (ReferenceEquals(this, _bitcoin.Value))
        {
            // Only use fast decode for lengths that STRONGLY suggest fixed sizes
            // These are the maximum-length encodings that are very likely to be exactly 32/64 bytes
            return encoded.Length switch
            {
                >= 43 and <= 44 => DecodeBitcoin32Fast(encoded) ?? DecodeGeneric(encoded), // Very likely 32 bytes
                >= 87 and <= 88 => DecodeBitcoin64Fast(encoded) ?? DecodeGeneric(encoded), // Very likely 64 bytes  
                _ => DecodeGeneric(encoded)
            };
        }

        // Fallback for other alphabets
        return DecodeGeneric(encoded);
    }

    /// <summary>
    /// Decode Base58 string to byte array using generic algorithm
    /// </summary>
    /// <param name="encoded">Base58 encoded string</param>
    /// <returns>Decoded byte array</returns>
    /// <exception cref="ArgumentException">Invalid Base58 character</exception>
    internal byte[] DecodeGeneric(ReadOnlySpan<char> encoded)
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

        // If we only have leading ones and no other digits were processed,
        // we should only return the leading zeros (not add an extra byte)
        int actualDecodedLength = (leadingOnes == encoded.Length) ? 0 : decodedLength;

        var result = new byte[leadingOnes + actualDecodedLength];

        if (actualDecodedLength > 0)
        {
            var finalDecoded = decoded.Slice(0, decodedLength);
            finalDecoded.Reverse();
            finalDecoded.CopyTo(result.AsSpan(leadingOnes));
        }

        return result;
    }

    internal static byte[]? DecodeBitcoin32Fast(ReadOnlySpan<char> encoded)
    {
        int charCount = encoded.Length;

        // Convert to raw base58 digits with validation + conversion in one pass
        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz32]; // 45 bytes
        var bitcoinDecodeTable = Base58Alphabet.Bitcoin.DecodeTable.Span;

        // Prepend zeros to make exactly Raw58Sz32 characters
        int prepend0 = Base58BitcoinTables.Raw58Sz32 - charCount;
        for (int j = 0; j < Base58BitcoinTables.Raw58Sz32; j++)
        {
            if (j < prepend0)
            {
                rawBase58[j] = 0;
            }
            else
            {
                char c = encoded[j - prepend0];
                // Validate + convert using Bitcoin decode table (return null for invalid chars)
                if (c >= 128 || bitcoinDecodeTable[c] == 255)
                    return null;

                rawBase58[j] = bitcoinDecodeTable[c];
            }
        }

        // Convert to intermediate format (base 58^5)
        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz32]; // 9 elements

        for (int i = 0; i < Base58BitcoinTables.IntermediateSz32; i++)
        {
            intermediate[i] = (ulong)rawBase58[5 * i + 0] * 11316496UL +   // 58^4
                             (ulong)rawBase58[5 * i + 1] * 195112UL +      // 58^3  
                             (ulong)rawBase58[5 * i + 2] * 3364UL +        // 58^2
                             (ulong)rawBase58[5 * i + 3] * 58UL +          // 58^1
                             (ulong)rawBase58[5 * i + 4] * 1UL;            // 58^0
        }

        // Convert to overcomplete base 2^32 using decode table
        Span<ulong> binary = stackalloc ulong[Base58BitcoinTables.BinarySz32]; // 8 elements

        for (int j = 0; j < Base58BitcoinTables.BinarySz32; j++)
        {
            ulong acc = 0UL;
            for (int i = 0; i < Base58BitcoinTables.IntermediateSz32; i++)
            {
                acc += intermediate[i] * Base58BitcoinTables.DecodeTable32[i][j];
            }
            binary[j] = acc;
        }

        // Reduce each term to less than 2^32
        for (int i = Base58BitcoinTables.BinarySz32 - 1; i > 0; i--)
        {
            binary[i - 1] += (binary[i] >> 32);
            binary[i] &= 0xFFFFFFFFUL;
        }

        // Check if the result is too large for 32 bytes
        if (binary[0] > 0xFFFFFFFFUL) return null;

        // Convert to big-endian byte output
        var result = new byte[32];
        for (int i = 0; i < Base58BitcoinTables.BinarySz32; i++)
        {
            uint value = (uint)binary[i];
            int offset = i * sizeof(uint);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(offset, sizeof(uint)), value);
        }

        // Count leading zeros in output
        int outputLeadingZeros = 0;
        for (int i = 0; i < 32; i++)
        {
            if (result[i] != 0) break;
            outputLeadingZeros++;
        }

        // Count leading '1's in input
        int inputLeadingOnes = 0;
        for (int i = 0; i < encoded.Length; i++)
        {
            if (encoded[i] != '1') break;
            inputLeadingOnes++;
        }

        // Leading zeros in output must match leading '1's in input
        if (outputLeadingZeros != inputLeadingOnes) return null;

        // Return the full 32 bytes - the result should always be 32 bytes for 32-byte decode
        return result;
    }

    internal static byte[]? DecodeBitcoin64Fast(ReadOnlySpan<char> encoded)
    {
        int charCount = encoded.Length;

        // Convert to raw base58 digits with validation + conversion in one pass
        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz64]; // 90 bytes
        var bitcoinDecodeTable = Base58Alphabet.Bitcoin.DecodeTable.Span;

        // Prepend zeros to make exactly Raw58Sz64 characters
        int prepend0 = Base58BitcoinTables.Raw58Sz64 - charCount;
        for (int j = 0; j < Base58BitcoinTables.Raw58Sz64; j++)
        {
            if (j < prepend0)
            {
                rawBase58[j] = 0;
            }
            else
            {
                char c = encoded[j - prepend0];
                // Validate + convert using Bitcoin decode table (return null for invalid chars)
                if (c >= 128 || bitcoinDecodeTable[c] == 255)
                    return null;

                rawBase58[j] = bitcoinDecodeTable[c];
            }
        }

        // Convert to intermediate format (base 58^5)
        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz64]; // 18 elements

        for (int i = 0; i < Base58BitcoinTables.IntermediateSz64; i++)
        {
            intermediate[i] = (ulong)rawBase58[5 * i + 0] * 11316496UL +   // 58^4
                             (ulong)rawBase58[5 * i + 1] * 195112UL +      // 58^3  
                             (ulong)rawBase58[5 * i + 2] * 3364UL +        // 58^2
                             (ulong)rawBase58[5 * i + 3] * 58UL +          // 58^1
                             (ulong)rawBase58[5 * i + 4] * 1UL;            // 58^0
        }

        // Convert to overcomplete base 2^32 using decode table
        Span<ulong> binary = stackalloc ulong[Base58BitcoinTables.BinarySz64]; // 16 elements

        for (int j = 0; j < Base58BitcoinTables.BinarySz64; j++)
        {
            ulong acc = 0UL;
            for (int i = 0; i < Base58BitcoinTables.IntermediateSz64; i++)
            {
                acc += intermediate[i] * Base58BitcoinTables.DecodeTable64[i][j];
            }
            binary[j] = acc;
        }

        // Reduce each term to less than 2^32
        for (int i = Base58BitcoinTables.BinarySz64 - 1; i > 0; i--)
        {
            binary[i - 1] += (binary[i] >> 32);
            binary[i] &= 0xFFFFFFFFUL;
        }

        // Check if the result is too large for 64 bytes
        if (binary[0] > 0xFFFFFFFFUL) return null;

        // Convert to big-endian byte output
        var result = new byte[64];
        for (int i = 0; i < Base58BitcoinTables.BinarySz64; i++)
        {
            uint value = (uint)binary[i];
            int offset = i * sizeof(uint);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(offset, sizeof(uint)), value);
        }

        // Count leading zeros in output
        int outputLeadingZeros = 0;
        for (int i = 0; i < 64; i++)
        {
            if (result[i] != 0) break;
            outputLeadingZeros++;
        }

        // Count leading '1's in input
        int inputLeadingOnes = 0;
        for (int i = 0; i < encoded.Length; i++)
        {
            if (encoded[i] != '1') break;
            inputLeadingOnes++;
        }

        // Leading zeros in output must match leading '1's in input
        if (outputLeadingZeros != inputLeadingOnes) return null;

        // Return the full 64 bytes - the result should always be 64 bytes for 64-byte decode
        return result;
    }
}
