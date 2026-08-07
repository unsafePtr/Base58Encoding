using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Base58Encoding;

public sealed partial class Base58<TAlphabet>
    where TAlphabet : struct, IBase58Alphabet
{
    /// <summary>
    /// Decodes a Base58 string to a new byte array.
    /// </summary>
    /// <param name="encoded">Base58 encoded input.</param>
    /// <returns>Decoded byte array.</returns>
    /// <exception cref="ArgumentException">Invalid Base58 character.</exception>
    public byte[] Decode(scoped ReadOnlySpan<char> encoded)
    {
        if (encoded.IsEmpty)
        {
            return [];
        }

        if (typeof(TAlphabet) == typeof(BitcoinAlphabet))
        {
            if (encoded.Length is >= 43 and <= 44)
            {
                Span<byte> buf = stackalloc byte[32];
                if (DecodeBitcoin32Fast(encoded, buf))
                {
                    return buf.ToArray();
                }
            }
            else if (encoded.Length is >= 87 and <= 88)
            {
                Span<byte> buf = stackalloc byte[64];
                if (DecodeBitcoin64Fast(encoded, buf))
                {
                    return buf.ToArray();
                }
            }
        }

        return DecodeGenericToArray(encoded);
    }

    /// <summary>
    /// Decodes Base58 chars into <paramref name="destination"/>.
    /// </summary>
    /// <param name="encoded">Base58 encoded input.</param>
    /// <param name="destination">Destination buffer for decoded bytes.</param>
    /// <returns>Number of bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown on invalid Base58 character or when <paramref name="destination"/> is too small.
    /// </exception>
    public int Decode(scoped ReadOnlySpan<char> encoded, scoped Span<byte> destination)
    {
        if (encoded.IsEmpty)
        {
            return 0;
        }

        return DecodeCore(encoded, destination);
    }

    /// <summary>
    /// Decodes Base58 ASCII bytes into <paramref name="destination"/>.
    /// </summary>
    /// <param name="encoded">Base58 encoded input as ASCII bytes.</param>
    /// <param name="destination">Destination buffer for decoded bytes.</param>
    /// <returns>Number of bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown on invalid Base58 character or when <paramref name="destination"/> is too small.
    /// </exception>
    public int Decode(scoped ReadOnlySpan<byte> encoded, scoped Span<byte> destination)
    {
        if (encoded.IsEmpty)
        {
            return 0;
        }

        return DecodeCore(encoded, destination);
    }

    private static int DecodeCore<TChar>(ReadOnlySpan<TChar> encoded, Span<byte> destination)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        if (typeof(TAlphabet) == typeof(BitcoinAlphabet))
        {
            if (encoded.Length is >= 43 and <= 44)
            {
                if (DecodeBitcoin32Fast(encoded, destination))
                {
                    return 32;
                }
            }
            else if (encoded.Length is >= 87 and <= 88)
            {
                if (DecodeBitcoin64Fast(encoded, destination))
                {
                    return 64;
                }
            }
        }

        return DecodeGenericCore(encoded, destination);
    }

    [SkipLocalsInit]
    private static int DecodeGenericCore<TChar>(ReadOnlySpan<TChar> encoded, Span<byte> destination)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        TChar firstChar = TChar.CreateTruncating(TAlphabet.FirstCharacter);
        int leadingOnes = Base58.CountLeadingCharacters(encoded, firstChar);
        int scratchSize = Base58.GetTypicalDecodedLength(encoded.Length);

        if (scratchSize <= MaxStackallocByte)
        {
            Span<byte> decoded = stackalloc byte[scratchSize];
            int decodedLength = ComputeGenericDecode(encoded, leadingOnes, decoded);
            int actualDecodedLength = leadingOnes == encoded.Length ? 0 : decodedLength;
            int totalLength = leadingOnes + actualDecodedLength;
            if (destination.Length < totalLength)
            {
                ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
            }

            EmitGenericDecode(destination, leadingOnes, decoded, actualDecodedLength);
            return totalLength;
        }

        return DecodeGenericCoreLarge(encoded, leadingOnes, scratchSize, destination);
    }

    private static int DecodeGenericCoreLarge<TChar>(ReadOnlySpan<TChar> encoded, int leadingOnes, int scratchSize, Span<byte> destination)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(scratchSize);
        try
        {
            int decodedLength = ComputeGenericDecode(encoded, leadingOnes, rented);
            int actualDecodedLength = leadingOnes == encoded.Length ? 0 : decodedLength;
            int totalLength = leadingOnes + actualDecodedLength;
            if (destination.Length < totalLength)
            {
                ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
            }

            EmitGenericDecode(destination, leadingOnes, rented, actualDecodedLength);
            return totalLength;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    [SkipLocalsInit]
    private static byte[] DecodeGenericToArray<TChar>(ReadOnlySpan<TChar> encoded)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        TChar firstChar = TChar.CreateTruncating(TAlphabet.FirstCharacter);
        int leadingOnes = Base58.CountLeadingCharacters(encoded, firstChar);

        if (leadingOnes == encoded.Length)
        {
            return new byte[leadingOnes];
        }

        int scratchSize = Base58.GetTypicalDecodedLength(encoded.Length);

        if (scratchSize <= MaxStackallocByte)
        {
            Span<byte> decoded = stackalloc byte[scratchSize];
            int decodedLength = ComputeGenericDecode(encoded, leadingOnes, decoded);
            byte[] result = new byte[leadingOnes + decodedLength];
            EmitGenericDecode(result, leadingOnes, decoded, decodedLength);
            return result;
        }

        return DecodeGenericToArrayLarge(encoded, leadingOnes, scratchSize);
    }

    private static byte[] DecodeGenericToArrayLarge<TChar>(ReadOnlySpan<TChar> encoded, int leadingOnes, int scratchSize)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(scratchSize);
        try
        {
            int decodedLength = ComputeGenericDecode(encoded, leadingOnes, rented);
            byte[] result = new byte[leadingOnes + decodedLength];
            EmitGenericDecode(result, leadingOnes, rented, decodedLength);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static int ComputeGenericDecode<TChar>(ReadOnlySpan<TChar> encoded, int leadingOnes, Span<byte> digits)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        int decodedLength = 1;
        digits[0] = 0;

        ReadOnlySpan<byte> decodeTable = TAlphabet.DecodeTable;

        for (int i = leadingOnes; i < encoded.Length; i++)
        {
            int c = int.CreateTruncating(encoded[i]);

            if ((uint)c >= 128 || decodeTable[c] == 255)
            {
                ThrowHelper.ThrowInvalidCharacter((char)c);
            }

            int carry = decodeTable[c];

            for (int j = 0; j < decodedLength; j++)
            {
                carry += digits[j] * Base;
                digits[j] = (byte)(carry & 0xFF);
                carry >>= 8;
            }

            while (carry > 0)
            {
                digits[decodedLength++] = (byte)(carry & 0xFF);
                carry >>= 8;
            }
        }

        return decodedLength;
    }

    private static void EmitGenericDecode(Span<byte> destination, int leadingOnes, Span<byte> digits, int decodedLength)
    {
        if (leadingOnes > 0)
        {
            destination[..leadingOnes].Clear();
        }

        if (decodedLength > 0)
        {
            Span<byte> finalDecoded = digits[..decodedLength];
            finalDecoded.Reverse();
            finalDecoded.CopyTo(destination[leadingOnes..]);
        }
    }

    /// <summary>
    /// Writes exactly 32 bytes and returns true on success, or false if the encoded input does not
    /// represent exactly 32 bytes, in which case the caller falls back to the generic decode.
    /// Throws on an invalid character, or on insufficient destination once the fast path commits.
    /// </summary>
    [SkipLocalsInit]
    internal static bool DecodeBitcoin32Fast<TChar>(ReadOnlySpan<TChar> encoded, Span<byte> destination)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        int charCount = encoded.Length;

        // Convert to raw base58 digits with validation + conversion in one pass
        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz32];
        ReadOnlySpan<byte> bitcoinDecodeTable = BitcoinAlphabet.DecodeTable;

        // Prepend zeros to make exactly Raw58Sz32 characters
        int prepend0 = Base58BitcoinTables.Raw58Sz32 - charCount;
        rawBase58[..prepend0].Clear();

        for (int i = 0; i < charCount; i++)
        {
            int c = int.CreateTruncating(encoded[i]);
            // Validate + convert using Bitcoin decode table
            if ((uint)c >= 128 || bitcoinDecodeTable[c] == 255)
            {
                ThrowHelper.ThrowInvalidCharacter((char)c);
            }

            rawBase58[prepend0 + i] = bitcoinDecodeTable[c];
        }

        // Convert to intermediate format (base 58^5)
        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz32];

        for (int i = 0; i < Base58BitcoinTables.IntermediateSz32; i++)
        {
            intermediate[i] = (ulong)rawBase58[5 * i + 0] * 11316496UL +   // 58^4
                              (ulong)rawBase58[5 * i + 1] * 195112UL +      // 58^3
                              (ulong)rawBase58[5 * i + 2] * 3364UL +        // 58^2
                              (ulong)rawBase58[5 * i + 3] * 58UL +          // 58^1
                              (ulong)rawBase58[5 * i + 4] * 1UL;            // 58^0
        }

        // Convert to overcomplete base 2^32 using decode table
        Span<ulong> binary = stackalloc ulong[Base58BitcoinTables.BinarySz32];

        for (int j = 0; j < Base58BitcoinTables.BinarySz32; j++)
        {
            binary[j] = VectorMath.TensorDot(intermediate, Base58BitcoinTables.DecodeTable32.AsSpan(j * Base58BitcoinTables.IntermediateSz32, Base58BitcoinTables.IntermediateSz32));
        }

        // Reduce each term to less than 2^32
        for (int i = Base58BitcoinTables.BinarySz32 - 1; i > 0; i--)
        {
            binary[i - 1] += binary[i] >> 32;
            binary[i] &= 0xFFFFFFFFUL;
        }

        // Check if the result is too large for 32 bytes
        if (binary[0] > 0xFFFFFFFFUL)
        {
            return false;
        }

        // Count leading zero bytes in the output directly from binary[] without materializing it.
        // Each limb is 4 bytes big-endian.
        int outputLeadingZeros = 0;
        for (int i = 0; i < Base58BitcoinTables.BinarySz32; i++)
        {
            uint v = (uint)binary[i];
            if (v != 0)
            {
                outputLeadingZeros += BitOperations.LeadingZeroCount(v) / 8;
                break;
            }
            outputLeadingZeros += 4;
        }

        // Leading zeros in output must match leading '1's in input.
        // Mismatch means this encoded string doesn't represent exactly 32 bytes.
        TChar one = TChar.CreateTruncating((byte)'1');
        int inputLeadingOnes = Base58.CountLeadingCharacters(encoded, one);

        if (outputLeadingZeros != inputLeadingOnes)
        {
            return false;
        }

        if (destination.Length < 32)
        {
            ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
        }

        // Convert to big-endian byte output
        for (int i = 0; i < Base58BitcoinTables.BinarySz32; i++)
        {
            uint value = (uint)binary[i];
            int offset = i * sizeof(uint);
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset, sizeof(uint)), value);
        }

        return true;
    }

    [SkipLocalsInit]
    internal static bool DecodeBitcoin64Fast<TChar>(ReadOnlySpan<TChar> encoded, Span<byte> destination)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        int charCount = encoded.Length;

        // Convert to raw base58 digits with validation + conversion in one pass
        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz64];
        ReadOnlySpan<byte> bitcoinDecodeTable = BitcoinAlphabet.DecodeTable;

        // Prepend zeros to make exactly Raw58Sz64 characters
        int prepend0 = Base58BitcoinTables.Raw58Sz64 - charCount;
        rawBase58[..prepend0].Clear();

        for (int i = 0; i < charCount; i++)
        {
            int c = int.CreateTruncating(encoded[i]);
            // Validate + convert using Bitcoin decode table
            if ((uint)c >= 128 || bitcoinDecodeTable[c] == 255)
            {
                ThrowHelper.ThrowInvalidCharacter((char)c);
            }

            rawBase58[prepend0 + i] = bitcoinDecodeTable[c];
        }

        // Convert to intermediate format (base 58^5)
        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz64];

        for (int i = 0; i < Base58BitcoinTables.IntermediateSz64; i++)
        {
            intermediate[i] = (ulong)rawBase58[5 * i + 0] * 11316496UL +   // 58^4
                              (ulong)rawBase58[5 * i + 1] * 195112UL +      // 58^3
                              (ulong)rawBase58[5 * i + 2] * 3364UL +        // 58^2
                              (ulong)rawBase58[5 * i + 3] * 58UL +          // 58^1
                              (ulong)rawBase58[5 * i + 4] * 1UL;            // 58^0
        }

        // Convert to overcomplete base 2^32 using decode table
        Span<ulong> binary = stackalloc ulong[Base58BitcoinTables.BinarySz64];

        for (int j = 0; j < Base58BitcoinTables.BinarySz64; j++)
        {
            binary[j] = VectorMath.TensorDot(intermediate, Base58BitcoinTables.DecodeTable64.AsSpan(j * Base58BitcoinTables.IntermediateSz64, Base58BitcoinTables.IntermediateSz64));
        }

        // Reduce each term to less than 2^32
        for (int i = Base58BitcoinTables.BinarySz64 - 1; i > 0; i--)
        {
            binary[i - 1] += binary[i] >> 32;
            binary[i] &= 0xFFFFFFFFUL;
        }

        // Check if the result is too large for 64 bytes
        if (binary[0] > 0xFFFFFFFFUL)
        {
            return false;
        }

        // Count leading zero bytes in the output directly from binary[] without materializing it.
        // Each limb is 4 bytes big-endian.
        int outputLeadingZeros = 0;
        for (int i = 0; i < Base58BitcoinTables.BinarySz64; i++)
        {
            uint v = (uint)binary[i];
            if (v != 0)
            {
                outputLeadingZeros += BitOperations.LeadingZeroCount(v) / 8;
                break;
            }
            outputLeadingZeros += 4;
        }

        // Leading zeros in output must match leading '1's in input.
        // Mismatch means this encoded string doesn't represent exactly 64 bytes.
        TChar one = TChar.CreateTruncating((byte)'1');
        int inputLeadingOnes = Base58.CountLeadingCharacters(encoded, one);

        if (outputLeadingZeros != inputLeadingOnes)
        {
            return false;
        }

        if (destination.Length < 64)
        {
            ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
        }

        // Convert to big-endian byte output
        for (int i = 0; i < Base58BitcoinTables.BinarySz64; i++)
        {
            uint value = (uint)binary[i];
            int offset = i * sizeof(uint);
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset, sizeof(uint)), value);
        }

        return true;
    }

    internal static byte[]? DecodeBitcoin32Fast(ReadOnlySpan<char> encoded)
    {
        Span<byte> buffer = stackalloc byte[32];
        return DecodeBitcoin32Fast<char>(encoded, buffer) ? buffer.ToArray() : null;
    }

    internal static byte[]? DecodeBitcoin64Fast(ReadOnlySpan<char> encoded)
    {
        Span<byte> buffer = stackalloc byte[64];
        return DecodeBitcoin64Fast<char>(encoded, buffer) ? buffer.ToArray() : null;
    }
}
