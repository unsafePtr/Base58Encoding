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
    public string Encode(scoped ReadOnlySpan<byte> data)
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
    public int Encode(scoped ReadOnlySpan<byte> data, scoped Span<byte> destination)
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
    private static string EncodeGenericToString(ReadOnlySpan<byte> data)
    {
        int leadingZeros = Base58.CountLeadingZeros(data);

        if (leadingZeros == data.Length)
        {
            return new string((char)TAlphabet.FirstCharacter, leadingZeros);
        }

        ReadOnlySpan<byte> inputSpan = data[leadingZeros..];
        int size = Base58.GetMaxEncodedLength(inputSpan.Length);

        if (size <= MaxStackallocByte)
        {
            Span<byte> digits = stackalloc byte[size];
            int digitCount = ComputeGenericDigits(inputSpan, digits);
            var state = new EncodeState<TAlphabet>(digits, 0, digitCount, leadingZeros);
            return string.Create(state.OutputLength, state, static (span, s) => s.EmitReverse(span));
        }

        return EncodeGenericToStringLarge(inputSpan, leadingZeros, size);
    }

    private static string EncodeGenericToStringLarge(ReadOnlySpan<byte> inputSpan, int leadingZeros, int size)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            int digitCount = ComputeGenericDigits(inputSpan, rented);
            var state = new EncodeState<TAlphabet>(rented, 0, digitCount, leadingZeros);
            return string.Create(state.OutputLength, state, static (span, s) => s.EmitReverse(span));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    [SkipLocalsInit]
    private static int EncodeGenericToBytes(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        int leadingZeros = Base58.CountLeadingZeros(data);

        if (leadingZeros == data.Length)
        {
            if (destination.Length < leadingZeros)
            {
                ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
            }

            destination[..leadingZeros].Fill(TAlphabet.FirstCharacter);
            return leadingZeros;
        }

        ReadOnlySpan<byte> inputSpan = data[leadingZeros..];
        int size = Base58.GetMaxEncodedLength(inputSpan.Length);

        if (size <= MaxStackallocByte)
        {
            Span<byte> digits = stackalloc byte[size];
            int digitCount = ComputeGenericDigits(inputSpan, digits);
            int outputLength = leadingZeros + digitCount;
            if (destination.Length < outputLength)
            {
                ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
            }

            var state = new EncodeState<TAlphabet>(digits, 0, digitCount, leadingZeros);
            state.EmitReverse(destination);
            return outputLength;
        }

        return EncodeGenericToBytesLarge(inputSpan, leadingZeros, size, destination);
    }

    private static int EncodeGenericToBytesLarge(ReadOnlySpan<byte> inputSpan, int leadingZeros, int size, Span<byte> destination)
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

            var state = new EncodeState<TAlphabet>(rented, 0, digitCount, leadingZeros);
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

        var state = new EncodeState<TAlphabet>(rawBase58, rawLeadingZeros, digitCount, inLeadingZeros);
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

        var state = new EncodeState<TAlphabet>(rawBase58, rawLeadingZeros, digitCount, inLeadingZeros);
        state.EmitForward(destination);
        return outputLength;
    }

    [SkipLocalsInit]
    private static int ComputeBitcoin32FastRaw(ReadOnlySpan<byte> data, Span<byte> rawBase58)
    {
        // Span params hide their length. Re-slicing to the fixed sizes folds away ~50 bounds checks
        // below: 30% less code, perf-neutral.
        data = data[..(Base58BitcoinTables.BinarySz32 * sizeof(uint))];
        rawBase58 = rawBase58[..Base58BitcoinTables.Raw58Sz32];

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

        // Matrix multiplication: for each source limb, add binary[i] * row into intermediate[1..].
        int rowLen = Base58BitcoinTables.IntermediateSz32 - 1;
        Span<ulong> acc = intermediate.Slice(1, rowLen);
        for (int i = 0; i < Base58BitcoinTables.BinarySz32; i++)
        {
            VectorMath.TensorMultiplyAdd(Base58BitcoinTables.EncodeTable32RowMajor.AsSpan(i * rowLen, rowLen), binary[i], acc);
        }

        // Reduce each term to be less than 58^5
        for (int i = Base58BitcoinTables.IntermediateSz32 - 1; i > 0; i--)
        {
            intermediate[i - 1] += intermediate[i] / Base58BitcoinTables.R1Div;
            intermediate[i] %= Base58BitcoinTables.R1Div;
        }

        // Convert intermediate form to raw base58 digits (5 digits per limb)
        VectorMath.ExtractBase58Digits(intermediate, rawBase58);

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

        var state = new EncodeState<TAlphabet>(rawBase58, rawLeadingZeros, digitCount, inLeadingZeros);
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

        var state = new EncodeState<TAlphabet>(rawBase58, rawLeadingZeros, digitCount, inLeadingZeros);
        state.EmitForward(destination);
        return outputLength;
    }

    [SkipLocalsInit]
    private static int ComputeBitcoin64FastRaw(ReadOnlySpan<byte> data, Span<byte> rawBase58)
    {
        // See ComputeBitcoin32FastRaw.
        data = data[..(Base58BitcoinTables.BinarySz64 * sizeof(uint))];
        rawBase58 = rawBase58[..Base58BitcoinTables.Raw58Sz64];

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

        int rowLen = Base58BitcoinTables.IntermediateSz64 - 1;
        Span<ulong> acc = intermediate.Slice(1, rowLen);
        for (int i = 0; i < 8; i++)
        {
            VectorMath.TensorMultiplyAdd(Base58BitcoinTables.EncodeTable64RowMajor.AsSpan(i * rowLen, rowLen), binary[i], acc);
        }

        // Mini-reduction to prevent overflow (like Firedancer)
        intermediate[15] += intermediate[16] / Base58BitcoinTables.R1Div;
        intermediate[16] %= Base58BitcoinTables.R1Div;

        for (int i = 8; i < Base58BitcoinTables.BinarySz64; i++)
        {
            VectorMath.TensorMultiplyAdd(Base58BitcoinTables.EncodeTable64RowMajor.AsSpan(i * rowLen, rowLen), binary[i], acc);
        }

        // Reduce each term to be less than 58^5
        for (int i = Base58BitcoinTables.IntermediateSz64 - 1; i > 0; i--)
        {
            intermediate[i - 1] += intermediate[i] / Base58BitcoinTables.R1Div;
            intermediate[i] %= Base58BitcoinTables.R1Div;
        }

        // Convert intermediate form to raw base58 digits (5 digits per limb)
        VectorMath.ExtractBase58Digits(intermediate, rawBase58);

        Debug.Assert(
            !rawBase58.ContainsAnyExceptInRange((byte)0, (byte)57),
            "Invalid base58 digit generated - algorithm bug");

        int rawLeadingZeros = 0;
        for (; rawLeadingZeros < Base58BitcoinTables.Raw58Sz64; rawLeadingZeros++)
        {
            if (rawBase58[rawLeadingZeros] != 0) break;
        }

        return rawLeadingZeros;
    }

    private readonly ref struct EncodeState<T>
        where T : struct, IBase58Alphabet
    {
        public readonly ReadOnlySpan<byte> Digits;
        public readonly int DigitStart;
        public readonly int DigitCount;
        public readonly int LeadingCount;

        public EncodeState(
            ReadOnlySpan<byte> digits,
            int digitStart,
            int digitCount,
            int leadingCount)
        {
            Digits = digits;
            DigitStart = digitStart;
            DigitCount = digitCount;
            LeadingCount = leadingCount;
        }

        public int OutputLength => LeadingCount + DigitCount;

        public void EmitForward<TChar>(Span<TChar> destination)
            where TChar : unmanaged, IBinaryInteger<TChar>
        {
            if (LeadingCount > 0)
            {
                destination[..LeadingCount].Fill(TChar.CreateTruncating(T.FirstCharacter));
            }

            int index = LeadingCount;
            int end = DigitStart + DigitCount;
            ReadOnlySpan<byte> alphabet = T.Characters;
            for (int i = DigitStart; i < end; i++)
            {
                destination[index++] = TChar.CreateTruncating((ushort)alphabet[Digits[i]]);
            }
        }

        public void EmitReverse<TChar>(Span<TChar> destination)
            where TChar : unmanaged, IBinaryInteger<TChar>
        {
            if (LeadingCount > 0)
            {
                destination[..LeadingCount].Fill(TChar.CreateTruncating(T.FirstCharacter));
            }

            int index = LeadingCount;
            ReadOnlySpan<byte> alphabet = T.Characters;
            for (int i = DigitStart + DigitCount - 1; i >= DigitStart; i--)
            {
                destination[index++] = TChar.CreateTruncating((ushort)alphabet[Digits[i]]);
            }
        }
    }
}
