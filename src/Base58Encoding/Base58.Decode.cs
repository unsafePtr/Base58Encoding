using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;

namespace Base58Encoding;

public partial class Base58
{
    /// <summary>
    /// Decodes a Base58 string to a new byte array.
    /// </summary>
    /// <param name="encoded">Base58 encoded input.</param>
    /// <returns>Decoded byte array.</returns>
    /// <exception cref="ArgumentException">Invalid Base58 character.</exception>
    public byte[] Decode(ReadOnlySpan<char> encoded)
    {
        if (encoded.IsEmpty)
        {
            return [];
        }

        // Bitcoin fast-path dispatch: allocate the fixed-size result up front
        // and write directly into it. On fallback we discard and re-decode.
        if (ReferenceEquals(this, _bitcoin.Value))
        {
            if (encoded.Length is >= 43 and <= 44)
            {
                Span<byte> buf = stackalloc byte[32];
                if (TryDecodeBitcoin32Fast<char>(encoded, buf) == 32)
                {
                    return buf.ToArray();
                }
            }
            else if (encoded.Length is >= 87 and <= 88)
            {
                Span<byte> buf = stackalloc byte[64];
                if (TryDecodeBitcoin64Fast<char>(encoded, buf) == 64)
                {
                    return buf.ToArray();
                }
            }
        }

        return DecodeGenericToArray<char>(encoded);
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
    public int Decode(ReadOnlySpan<char> encoded, Span<byte> destination)
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
    public int Decode(ReadOnlySpan<byte> encoded, Span<byte> destination)
    {
        if (encoded.IsEmpty)
        {
            return 0;
        }

        return DecodeCore(encoded, destination);
    }

    private int DecodeCore<TChar>(ReadOnlySpan<TChar> encoded, Span<byte> destination)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        if (ReferenceEquals(this, _bitcoin.Value))
        {
            if (encoded.Length is >= 43 and <= 44)
            {
                int r = TryDecodeBitcoin32Fast(encoded, destination);
                if (r >= 0)
                {
                    return r;
                }
            }
            else if (encoded.Length is >= 87 and <= 88)
            {
                int r = TryDecodeBitcoin64Fast(encoded, destination);
                if (r >= 0)
                {
                    return r;
                }
            }
        }

        return DecodeGenericCore(encoded, destination);
    }

    private int DecodeGenericCore<TChar>(ReadOnlySpan<TChar> encoded, Span<byte> destination)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        TChar firstChar = TChar.CreateTruncating((ushort)_firstCharacter);
        int leadingOnes = CountLeading(encoded, firstChar);

        int scratchSize = encoded.Length * 733 / 1000 + 1;
        byte[]? rented = null;
        try
        {
            Span<byte> decoded = scratchSize > MaxStackallocByte
                ? (rented = ArrayPool<byte>.Shared.Rent(scratchSize))
                : stackalloc byte[scratchSize];

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
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }
    }

    private byte[] DecodeGenericToArray<TChar>(ReadOnlySpan<TChar> encoded)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        TChar firstChar = TChar.CreateTruncating((ushort)_firstCharacter);
        int leadingOnes = CountLeading(encoded, firstChar);

        if (leadingOnes == encoded.Length)
        {
            return new byte[leadingOnes];
        }

        int scratchSize = encoded.Length * 733 / 1000 + 1;
        byte[]? rented = null;
        try
        {
            Span<byte> decoded = scratchSize > MaxStackallocByte
                ? (rented = ArrayPool<byte>.Shared.Rent(scratchSize))
                : stackalloc byte[scratchSize];

            int decodedLength = ComputeGenericDecode(encoded, leadingOnes, decoded);

            // Allocate exact-sized heap result, then emit directly into it — single copy.
            byte[] result = new byte[leadingOnes + decodedLength];
            EmitGenericDecode(result, leadingOnes, decoded, decodedLength);
            return result;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }
    }

    private int ComputeGenericDecode<TChar>(ReadOnlySpan<TChar> encoded, int leadingOnes, Span<byte> digits)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        int decodedLength = 1;
        digits[0] = 0;

        ReadOnlySpan<byte> decodeTable = _decodeTable.Span;

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

    private static int CountLeading<TChar>(ReadOnlySpan<TChar> text, TChar target)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        int mismatchIndex = text.IndexOfAnyExcept(target);
        return mismatchIndex == -1 ? text.Length : mismatchIndex;
    }

    /// <summary>
    /// Returns bytes written (32) on success, or -1 if the encoded input doesn't
    /// represent exactly 32 bytes (caller should fall back to generic decode).
    /// Throws on invalid character or insufficient destination when fast path matches.
    /// </summary>
    private static int TryDecodeBitcoin32Fast<TChar>(ReadOnlySpan<TChar> encoded, Span<byte> destination)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        int charCount = encoded.Length;

        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz32];
        ReadOnlySpan<byte> bitcoinDecodeTable = Base58Alphabet.Bitcoin.DecodeTable.Span;

        int prepend0 = Base58BitcoinTables.Raw58Sz32 - charCount;
        for (int j = 0; j < Base58BitcoinTables.Raw58Sz32; j++)
        {
            if (j < prepend0)
            {
                rawBase58[j] = 0;
            }
            else
            {
                int c = int.CreateTruncating(encoded[j - prepend0]);
                if ((uint)c >= 128 || bitcoinDecodeTable[c] == 255)
                {
                    ThrowHelper.ThrowInvalidCharacter((char)c);
                }

                rawBase58[j] = bitcoinDecodeTable[c];
            }
        }

        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz32];

        for (int i = 0; i < Base58BitcoinTables.IntermediateSz32; i++)
        {
            intermediate[i] = (ulong)rawBase58[5 * i + 0] * 11316496UL +
                              (ulong)rawBase58[5 * i + 1] * 195112UL +
                              (ulong)rawBase58[5 * i + 2] * 3364UL +
                              (ulong)rawBase58[5 * i + 3] * 58UL +
                              (ulong)rawBase58[5 * i + 4] * 1UL;
        }

        Span<ulong> binary = stackalloc ulong[Base58BitcoinTables.BinarySz32];

        for (int j = 0; j < Base58BitcoinTables.BinarySz32; j++)
        {
            ulong acc = 0UL;
            for (int i = 0; i < Base58BitcoinTables.IntermediateSz32; i++)
            {
                acc += intermediate[i] * Base58BitcoinTables.DecodeTable32[i][j];
            }
            binary[j] = acc;
        }

        for (int i = Base58BitcoinTables.BinarySz32 - 1; i > 0; i--)
        {
            binary[i - 1] += binary[i] >> 32;
            binary[i] &= 0xFFFFFFFFUL;
        }

        if (binary[0] > 0xFFFFFFFFUL)
        {
            return -1;
        }

        // Count leading zero BYTES in the 32-byte output directly from binary[]
        // without materializing the output. Each limb is 4 bytes big-endian.
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

        TChar one = TChar.CreateTruncating((ushort)'1');
        int inputLeadingOnes = CountLeading(encoded, one);

        if (outputLeadingZeros != inputLeadingOnes)
        {
            return -1;
        }

        if (destination.Length < 32)
        {
            ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
        }

        for (int i = 0; i < Base58BitcoinTables.BinarySz32; i++)
        {
            uint value = (uint)binary[i];
            int offset = i * sizeof(uint);
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset, sizeof(uint)), value);
        }

        return 32;
    }

    private static int TryDecodeBitcoin64Fast<TChar>(ReadOnlySpan<TChar> encoded, Span<byte> destination)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        int charCount = encoded.Length;

        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz64];
        ReadOnlySpan<byte> bitcoinDecodeTable = Base58Alphabet.Bitcoin.DecodeTable.Span;

        int prepend0 = Base58BitcoinTables.Raw58Sz64 - charCount;
        for (int j = 0; j < Base58BitcoinTables.Raw58Sz64; j++)
        {
            if (j < prepend0)
            {
                rawBase58[j] = 0;
            }
            else
            {
                int c = int.CreateTruncating(encoded[j - prepend0]);
                if ((uint)c >= 128 || bitcoinDecodeTable[c] == 255)
                {
                    ThrowHelper.ThrowInvalidCharacter((char)c);
                }

                rawBase58[j] = bitcoinDecodeTable[c];
            }
        }

        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz64];

        for (int i = 0; i < Base58BitcoinTables.IntermediateSz64; i++)
        {
            intermediate[i] = (ulong)rawBase58[5 * i + 0] * 11316496UL +
                              (ulong)rawBase58[5 * i + 1] * 195112UL +
                              (ulong)rawBase58[5 * i + 2] * 3364UL +
                              (ulong)rawBase58[5 * i + 3] * 58UL +
                              (ulong)rawBase58[5 * i + 4] * 1UL;
        }

        Span<ulong> binary = stackalloc ulong[Base58BitcoinTables.BinarySz64];

        for (int j = 0; j < Base58BitcoinTables.BinarySz64; j++)
        {
            ulong acc = 0UL;
            for (int i = 0; i < Base58BitcoinTables.IntermediateSz64; i++)
            {
                acc += intermediate[i] * Base58BitcoinTables.DecodeTable64[i][j];
            }
            binary[j] = acc;
        }

        for (int i = Base58BitcoinTables.BinarySz64 - 1; i > 0; i--)
        {
            binary[i - 1] += binary[i] >> 32;
            binary[i] &= 0xFFFFFFFFUL;
        }

        if (binary[0] > 0xFFFFFFFFUL)
        {
            return -1;
        }

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

        TChar one = TChar.CreateTruncating((ushort)'1');
        int inputLeadingOnes = CountLeading(encoded, one);

        if (outputLeadingZeros != inputLeadingOnes)
        {
            return -1;
        }

        if (destination.Length < 64)
        {
            ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
        }

        for (int i = 0; i < Base58BitcoinTables.BinarySz64; i++)
        {
            uint value = (uint)binary[i];
            int offset = i * sizeof(uint);
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset, sizeof(uint)), value);
        }

        return 64;
    }

    internal byte[] DecodeGeneric(ReadOnlySpan<char> encoded)
    {
        if (encoded.IsEmpty)
        {
            return [];
        }

        return DecodeGenericToArray<char>(encoded);
    }

    internal static byte[]? DecodeBitcoin32Fast(ReadOnlySpan<char> encoded)
    {
        Span<byte> buffer = stackalloc byte[32];
        int r = TryDecodeBitcoin32Fast<char>(encoded, buffer);
        return r < 0 ? null : buffer.ToArray();
    }

    internal static byte[]? DecodeBitcoin64Fast(ReadOnlySpan<char> encoded)
    {
        Span<byte> buffer = stackalloc byte[64];
        int r = TryDecodeBitcoin64Fast<char>(encoded, buffer);
        return r < 0 ? null : buffer.ToArray();
    }
}
