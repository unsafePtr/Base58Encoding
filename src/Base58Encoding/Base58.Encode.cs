using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Base58Encoding;

public sealed partial class Base58<TAlphabet>
    where TAlphabet : struct, IBase58Alphabet
{
    /// <summary>
    /// Encodes bytes to a Base58 string.
    /// </summary>
    /// <param name="data">Bytes to encode.</param>
    /// <returns>Base58 encoded string.</returns>
    public string Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        if (typeof(TAlphabet) == typeof(BitcoinAlphabet))
        {
            return data.Length switch
            {
                32 => EncodeBitcoin32FastToString(data),
                64 => EncodeBitcoin64FastToString(data),
                _ => EncodeGenericToString(data)
            };
        }

        return EncodeGenericToString(data);
    }

    /// <summary>
    /// Encodes bytes to Base58 ASCII bytes written into <paramref name="destination"/>.
    /// </summary>
    /// <param name="data">Bytes to encode.</param>
    /// <param name="destination">Destination buffer for ASCII-encoded Base58 characters.</param>
    /// <returns>Number of bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="destination"/> is too small.</exception>
    public int Encode(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        if (typeof(TAlphabet) == typeof(BitcoinAlphabet))
        {
            return data.Length switch
            {
                32 => EncodeBitcoin32FastToBytes(data, destination),
                64 => EncodeBitcoin64FastToBytes(data, destination),
                _ => EncodeGenericToBytes(data, destination)
            };
        }

        return EncodeGenericToBytes(data, destination);
    }

    [SkipLocalsInit]
    private string EncodeGenericToString(ReadOnlySpan<byte> data)
    {
        int leadingZeros = Base58.CountLeadingZeros(data);

        if (leadingZeros == data.Length)
        {
            return new string(TAlphabet.FirstCharacter, leadingZeros);
        }

        ReadOnlySpan<byte> inputSpan = data[leadingZeros..];
        int size = inputSpan.Length * 137 / 100 + 1;

        if (size <= MaxStackallocByte)
        {
            Span<byte> digits = stackalloc byte[size];
            int digitCount = ComputeGenericDigits(inputSpan, digits);
            var state = new EncodeState(digits, 0, digitCount, TAlphabet.Characters, TAlphabet.FirstCharacter, leadingZeros);
            return string.Create(state.OutputLength, state, static (span, s) => s.EmitReverse(span));
        }

        return EncodeGenericToStringLarge(inputSpan, leadingZeros, size);
    }

    private string EncodeGenericToStringLarge(ReadOnlySpan<byte> inputSpan, int leadingZeros, int size)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            int digitCount = ComputeGenericDigits(inputSpan, rented);
            var state = new EncodeState(rented, 0, digitCount, TAlphabet.Characters, TAlphabet.FirstCharacter, leadingZeros);
            return string.Create(state.OutputLength, state, static (span, s) => s.EmitReverse(span));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    [SkipLocalsInit]
    private int EncodeGenericToBytes(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        int leadingZeros = Base58.CountLeadingZeros(data);
        byte firstByte = (byte)TAlphabet.FirstCharacter;

        if (leadingZeros == data.Length)
        {
            if (destination.Length < leadingZeros)
            {
                ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
            }

            destination[..leadingZeros].Fill(firstByte);
            return leadingZeros;
        }

        ReadOnlySpan<byte> inputSpan = data[leadingZeros..];
        int size = inputSpan.Length * 137 / 100 + 1;

        if (size <= MaxStackallocByte)
        {
            Span<byte> digits = stackalloc byte[size];
            int digitCount = ComputeGenericDigits(inputSpan, digits);
            int outputLength = leadingZeros + digitCount;
            if (destination.Length < outputLength)
            {
                ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
            }
            var state = new EncodeState(digits, 0, digitCount, TAlphabet.Characters, TAlphabet.FirstCharacter, leadingZeros);
            state.EmitReverse(destination);
            return outputLength;
        }

        return EncodeGenericToBytesLarge(inputSpan, leadingZeros, size, destination);
    }

    private int EncodeGenericToBytesLarge(ReadOnlySpan<byte> inputSpan, int leadingZeros, int size, Span<byte> destination)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            int digitCount = ComputeGenericDigits(inputSpan, rented);
            int outputLength = leadingZeros + digitCount;
            if (destination.Length < outputLength)
            {
                ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
            }
            var state = new EncodeState(rented, 0, digitCount, TAlphabet.Characters, TAlphabet.FirstCharacter, leadingZeros);
            state.EmitReverse(destination);
            return outputLength;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static int ComputeGenericDigits(ReadOnlySpan<byte> inputSpan, Span<byte> digits)
    {
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

        return digitCount;
    }

    [SkipLocalsInit]
    internal static string EncodeBitcoin32FastToString(ReadOnlySpan<byte> data)
    {
        int inLeadingZeros = Base58.CountLeadingZeros(data);

        if (inLeadingZeros == data.Length)
        {
            return new string('1', inLeadingZeros);
        }

        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz32];
        int rawLeadingZeros = ComputeBitcoin32FastRaw(data, rawBase58);

        int skip = rawLeadingZeros - inLeadingZeros;
        Debug.Assert(skip >= 0, "rawLeadingZeros should always be >= inLeadingZeros by Base58 math");
        int digitCount = Base58BitcoinTables.Raw58Sz32 - rawLeadingZeros;

        var state = new EncodeState(rawBase58, rawLeadingZeros, digitCount, Base58BitcoinTables.BitcoinChars, '1', inLeadingZeros);
        return string.Create(state.OutputLength, state, static (span, s) => s.EmitForward(span));
    }

    [SkipLocalsInit]
    private static int EncodeBitcoin32FastToBytes(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        int inLeadingZeros = Base58.CountLeadingZeros(data);

        if (inLeadingZeros == data.Length)
        {
            if (destination.Length < inLeadingZeros)
            {
                ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
            }

            destination[..inLeadingZeros].Fill((byte)'1');
            return inLeadingZeros;
        }

        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz32];
        int rawLeadingZeros = ComputeBitcoin32FastRaw(data, rawBase58);

        int skip = rawLeadingZeros - inLeadingZeros;
        Debug.Assert(skip >= 0, "rawLeadingZeros should always be >= inLeadingZeros by Base58 math");
        int digitCount = Base58BitcoinTables.Raw58Sz32 - rawLeadingZeros;
        int outputLength = inLeadingZeros + digitCount;

        if (destination.Length < outputLength)
        {
            ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
        }

        var state = new EncodeState(rawBase58, rawLeadingZeros, digitCount, Base58BitcoinTables.BitcoinChars, '1', inLeadingZeros);
        state.EmitForward(destination);
        return outputLength;
    }

    [SkipLocalsInit]
    private static int ComputeBitcoin32FastRaw(ReadOnlySpan<byte> data, Span<byte> rawBase58)
    {
        // Convert 32 bytes to 8 uint32 limbs (big-endian)
        Span<uint> binary = stackalloc uint[Base58BitcoinTables.BinarySz32];
        for (int i = 0; i < Base58BitcoinTables.BinarySz32; i++)
        {
            int offset = i * sizeof(uint);
            binary[i] = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, sizeof(uint)));
        }

        // Convert to intermediate format (base 58^5)
        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz32];
        intermediate.Clear();

        // Matrix multiplication: intermediate = binary * EncodeTable32
        for (int i = 0; i < Base58BitcoinTables.BinarySz32; i++)
        {
            for (int j = 0; j < Base58BitcoinTables.IntermediateSz32 - 1; j++)
            {
                intermediate[j + 1] += (ulong)binary[i] * Base58BitcoinTables.EncodeTable32[i][j];
            }
        }

        // Reduce each term to be less than 58^5
        for (int i = Base58BitcoinTables.IntermediateSz32 - 1; i > 0; i--)
        {
            intermediate[i - 1] += intermediate[i] / Base58BitcoinTables.R1Div;
            intermediate[i] %= Base58BitcoinTables.R1Div;
        }

        // Convert intermediate form to raw base58 digits (5 digits per limb)
        for (int i = 0; i < Base58BitcoinTables.IntermediateSz32; i++)
        {
            uint v = (uint)intermediate[i];

            rawBase58[5 * i + 4] = (byte)((v / 1U) % 58U);
            rawBase58[5 * i + 3] = (byte)((v / 58U) % 58U);
            rawBase58[5 * i + 2] = (byte)((v / 3364U) % 58U);
            rawBase58[5 * i + 1] = (byte)((v / 195112U) % 58U);
            rawBase58[5 * i + 0] = (byte)(v / 11316496U);
        }

        // Count leading zeros in raw output — some come from input zero bytes,
        // some are mathematical padding (45-digit form slightly overshoots 44 chars max).
        int rawLeadingZeros = 0;
        for (; rawLeadingZeros < Base58BitcoinTables.Raw58Sz32; rawLeadingZeros++)
        {
            if (rawBase58[rawLeadingZeros] != 0) break;
        }

        return rawLeadingZeros;
    }

    [SkipLocalsInit]
    internal static string EncodeBitcoin64FastToString(ReadOnlySpan<byte> data)
    {
        int inLeadingZeros = Base58.CountLeadingZeros(data);

        if (inLeadingZeros == data.Length)
        {
            return new string('1', inLeadingZeros);
        }

        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz64];
        int rawLeadingZeros = ComputeBitcoin64FastRaw(data, rawBase58);

        int skip = rawLeadingZeros - inLeadingZeros;
        Debug.Assert(skip >= 0, "rawLeadingZeros should always be >= inLeadingZeros by Base58 math");
        int digitCount = Base58BitcoinTables.Raw58Sz64 - rawLeadingZeros;

        var state = new EncodeState(rawBase58, rawLeadingZeros, digitCount, Base58BitcoinTables.BitcoinChars, '1', inLeadingZeros);
        return string.Create(state.OutputLength, state, static (span, s) => s.EmitForward(span));
    }

    [SkipLocalsInit]
    private static int EncodeBitcoin64FastToBytes(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        int inLeadingZeros = Base58.CountLeadingZeros(data);

        if (inLeadingZeros == data.Length)
        {
            if (destination.Length < inLeadingZeros)
            {
                ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
            }

            destination[..inLeadingZeros].Fill((byte)'1');
            return inLeadingZeros;
        }

        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz64];
        int rawLeadingZeros = ComputeBitcoin64FastRaw(data, rawBase58);

        int skip = rawLeadingZeros - inLeadingZeros;
        Debug.Assert(skip >= 0, "rawLeadingZeros should always be >= inLeadingZeros by Base58 math");
        int digitCount = Base58BitcoinTables.Raw58Sz64 - rawLeadingZeros;
        int outputLength = inLeadingZeros + digitCount;

        if (destination.Length < outputLength)
        {
            ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
        }

        var state = new EncodeState(rawBase58, rawLeadingZeros, digitCount, Base58BitcoinTables.BitcoinChars, '1', inLeadingZeros);
        state.EmitForward(destination);
        return outputLength;
    }

    [SkipLocalsInit]
    private static int ComputeBitcoin64FastRaw(ReadOnlySpan<byte> data, Span<byte> rawBase58)
    {
        // Convert 64 bytes to 16 uint32 limbs (big-endian)
        Span<uint> binary = stackalloc uint[Base58BitcoinTables.BinarySz64];
        for (int i = 0; i < Base58BitcoinTables.BinarySz64; i++)
        {
            int offset = i * sizeof(uint);
            binary[i] = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, sizeof(uint)));
        }

        // Convert to intermediate format (base 58^5). For 64-byte input we must
        // split the matrix multiplication and interleave a mini-reduction to
        // keep intermediate limbs from overflowing (matches Firedancer exactly).
        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz64];
        intermediate.Clear();

        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < Base58BitcoinTables.IntermediateSz64 - 1; j++)
            {
                intermediate[j + 1] += (ulong)binary[i] * Base58BitcoinTables.EncodeTable64[i][j];
            }
        }

        // Mini-reduction to prevent overflow (like Firedancer)
        intermediate[15] += intermediate[16] / Base58BitcoinTables.R1Div;
        intermediate[16] %= Base58BitcoinTables.R1Div;

        // Finish remaining iterations
        for (int i = 8; i < Base58BitcoinTables.BinarySz64; i++)
        {
            for (int j = 0; j < Base58BitcoinTables.IntermediateSz64 - 1; j++)
            {
                intermediate[j + 1] += (ulong)binary[i] * Base58BitcoinTables.EncodeTable64[i][j];
            }
        }

        // Reduce each term to be less than 58^5
        for (int i = Base58BitcoinTables.IntermediateSz64 - 1; i > 0; i--)
        {
            intermediate[i - 1] += intermediate[i] / Base58BitcoinTables.R1Div;
            intermediate[i] %= Base58BitcoinTables.R1Div;
        }

        // Convert intermediate form to raw base58 digits (5 digits per limb)
        for (int i = 0; i < Base58BitcoinTables.IntermediateSz64; i++)
        {
            uint v = (uint)intermediate[i];
            rawBase58[5 * i + 4] = (byte)((v / 1U) % 58U);
            rawBase58[5 * i + 3] = (byte)((v / 58U) % 58U);
            rawBase58[5 * i + 2] = (byte)((v / 3364U) % 58U);
            rawBase58[5 * i + 1] = (byte)((v / 195112U) % 58U);
            rawBase58[5 * i + 0] = (byte)(v / 11316496U);

            Debug.Assert(rawBase58[5 * i + 0] < 58 && rawBase58[5 * i + 1] < 58 &&
                         rawBase58[5 * i + 2] < 58 && rawBase58[5 * i + 3] < 58 &&
                         rawBase58[5 * i + 4] < 58,
                         $"Invalid base58 digit generated at position {i} - algorithm bug");
        }

        int rawLeadingZeros = 0;
        for (; rawLeadingZeros < Base58BitcoinTables.Raw58Sz64; rawLeadingZeros++)
        {
            if (rawBase58[rawLeadingZeros] != 0) break;
        }

        return rawLeadingZeros;
    }

    private readonly ref struct EncodeState
    {
        public readonly ReadOnlySpan<byte> Digits;
        public readonly ReadOnlySpan<char> Alphabet;
        public readonly int DigitStart;
        public readonly int DigitCount;
        public readonly char LeadingFill;
        public readonly int LeadingCount;

        public EncodeState(
            ReadOnlySpan<byte> digits,
            int digitStart,
            int digitCount,
            ReadOnlySpan<char> alphabet,
            char leadingFill,
            int leadingCount)
        {
            Digits = digits;
            DigitStart = digitStart;
            DigitCount = digitCount;
            Alphabet = alphabet;
            LeadingFill = leadingFill;
            LeadingCount = leadingCount;
        }

        public int OutputLength => LeadingCount + DigitCount;

        public void EmitForward<TChar>(Span<TChar> destination)
            where TChar : unmanaged, IBinaryInteger<TChar>
        {
            if (LeadingCount > 0)
            {
                destination[..LeadingCount].Fill(TChar.CreateTruncating((ushort)LeadingFill));
            }

            int index = LeadingCount;
            int end = DigitStart + DigitCount;
            for (int i = DigitStart; i < end; i++)
            {
                destination[index++] = TChar.CreateTruncating((ushort)Alphabet[Digits[i]]);
            }
        }

        public void EmitReverse<TChar>(Span<TChar> destination)
            where TChar : unmanaged, IBinaryInteger<TChar>
        {
            if (LeadingCount > 0)
            {
                destination[..LeadingCount].Fill(TChar.CreateTruncating((ushort)LeadingFill));
            }

            int index = LeadingCount;
            for (int i = DigitStart + DigitCount - 1; i >= DigitStart; i--)
            {
                destination[index++] = TChar.CreateTruncating((ushort)Alphabet[Digits[i]]);
            }
        }
    }
}
