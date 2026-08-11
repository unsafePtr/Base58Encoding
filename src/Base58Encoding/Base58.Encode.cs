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
        int rawSize = GetGenericRawSize(inputSpan.Length);

        if (rawSize <= MaxStackallocByte)
        {
            Span<uint> limbs = stackalloc uint[rawSize / Base58Pow5Digits];
            Span<byte> raw = stackalloc byte[rawSize];
            int rawLeadingZeros = ComputeGenericRaw(inputSpan, limbs, raw);
            var state = new EncodeState<TAlphabet>(raw, rawLeadingZeros, rawSize - rawLeadingZeros, leadingZeros);
            return string.Create(state.OutputLength, state, static (span, s) => s.EmitForward(span));
        }

        return EncodeGenericToStringLarge(inputSpan, leadingZeros, rawSize);
    }

    private static string EncodeGenericToStringLarge(ReadOnlySpan<byte> inputSpan, int leadingZeros, int rawSize)
    {
        int limbCapacity = rawSize / Base58Pow5Digits;
        byte[] rentedRaw = ArrayPool<byte>.Shared.Rent(rawSize);
        uint[] rentedLimbs = ArrayPool<uint>.Shared.Rent(limbCapacity);
        try
        {
            // Rent overshoots, and the raw layout is right-aligned in exactly rawSize digits,
            // so both buffers must be sliced back to the requested size.
            Span<byte> raw = rentedRaw.AsSpan(0, rawSize);
            int rawLeadingZeros = ComputeGenericRaw(inputSpan, rentedLimbs.AsSpan(0, limbCapacity), raw);
            var state = new EncodeState<TAlphabet>(raw, rawLeadingZeros, rawSize - rawLeadingZeros, leadingZeros);
            return string.Create(state.OutputLength, state, static (span, s) => s.EmitForward(span));
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(rentedLimbs);
            ArrayPool<byte>.Shared.Return(rentedRaw);
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
        int rawSize = GetGenericRawSize(inputSpan.Length);

        if (rawSize <= MaxStackallocByte)
        {
            Span<uint> limbs = stackalloc uint[rawSize / Base58Pow5Digits];
            Span<byte> raw = stackalloc byte[rawSize];
            int rawLeadingZeros = ComputeGenericRaw(inputSpan, limbs, raw);
            int digitCount = rawSize - rawLeadingZeros;
            int outputLength = leadingZeros + digitCount;
            if (destination.Length < outputLength)
            {
                ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
            }

            var state = new EncodeState<TAlphabet>(raw, rawLeadingZeros, digitCount, leadingZeros);
            state.EmitForward(destination);
            return outputLength;
        }

        return EncodeGenericToBytesLarge(inputSpan, leadingZeros, rawSize, destination);
    }

    private static int EncodeGenericToBytesLarge(ReadOnlySpan<byte> inputSpan, int leadingZeros, int rawSize, Span<byte> destination)
    {
        int limbCapacity = rawSize / Base58Pow5Digits;
        byte[] rentedRaw = ArrayPool<byte>.Shared.Rent(rawSize);
        uint[] rentedLimbs = ArrayPool<uint>.Shared.Rent(limbCapacity);
        try
        {
            Span<byte> raw = rentedRaw.AsSpan(0, rawSize);
            int rawLeadingZeros = ComputeGenericRaw(inputSpan, rentedLimbs.AsSpan(0, limbCapacity), raw);
            int digitCount = rawSize - rawLeadingZeros;
            int outputLength = leadingZeros + digitCount;
            if (destination.Length < outputLength)
            {
                ThrowHelper.ThrowDestinationTooSmall(nameof(destination));
            }

            var state = new EncodeState<TAlphabet>(raw, rawLeadingZeros, digitCount, leadingZeros);
            state.EmitForward(destination);
            return outputLength;
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(rentedLimbs);
            ArrayPool<byte>.Shared.Return(rentedRaw);
        }
    }

    /// <summary>
    /// Scratch geometry for the generic path. The value is built in base 58^5 — the largest power
    /// of 58 that fits a <see cref="uint"/> — so one limb expands to exactly five base-58 digits and
    /// the raw digit buffer is the digit bound rounded up to a multiple of five.
    /// </summary>
    private static int GetGenericRawSize(int byteCount)
    {
        long rawSize = ((long)Base58.GetMaxEncodedLength(byteCount) + (Base58Pow5Digits - 1))
                       / Base58Pow5Digits * Base58Pow5Digits;

        if (rawSize > int.MaxValue)
        {
            ThrowHelper.ThrowInputTooLarge(nameof(byteCount));
        }

        return (int)rawSize;
    }

    /// <summary>
    /// Builds <paramref name="inputSpan"/> as a base-58^5 number in <paramref name="limbs"/> (least
    /// significant first), then expands every limb into five base-58 digits written most significant
    /// first and right-aligned in <paramref name="raw"/>. Returns the count of leading zero digits.
    /// </summary>
    /// <remarks>
    /// Consuming four input bytes per outer step and five output digits per limb keeps the inner
    /// carry loop roughly an order of magnitude shorter than a byte-at-a-time base-58 schoolbook
    /// pass, which is what dominates this path for the common 25-byte and 34-byte inputs.
    /// </remarks>
    private static int ComputeGenericRaw(ReadOnlySpan<byte> inputSpan, Span<uint> limbs, Span<byte> raw)
    {
        int limbCount = 1;
        limbs[0] = 0;

        // A leading partial word of one to three bytes is below 2^24, so it is already a valid limb.
        int head = inputSpan.Length & (sizeof(uint) - 1);
        if (head != 0)
        {
            uint w = 0;
            for (int i = 0; i < head; i++)
            {
                w = (w << 8) | inputSpan[i];
            }

            limbs[0] = w;
        }

        for (int offset = head; offset < inputSpan.Length; offset += sizeof(uint))
        {
            // value = value * 2^32 + word. The carry stays below 2^32, so cur stays below
            // 58^5 * 2^32 = 2.82e18 and never overflows a ulong.
            ulong carry = BinaryPrimitives.ReadUInt32BigEndian(inputSpan.Slice(offset, sizeof(uint)));

            for (int i = 0; i < limbCount; i++)
            {
                ulong cur = ((ulong)limbs[i] << 32) + carry;
                ulong q = cur / Base58Pow5;
                limbs[i] = (uint)(cur - (q * Base58Pow5));
                carry = q;
            }

            // carry < 2^32 < 7 * 58^5, so at most two new limbs appear per word.
            while (carry != 0)
            {
                ulong q = carry / Base58Pow5;
                limbs[limbCount++] = (uint)(carry - (q * Base58Pow5));
                carry = q;
            }
        }

        // The limb bound overshoots the true limb count by at most a limb or two. Leaving those
        // limbs unwritten at the front costs nothing: they are exactly the leading zeros we skip.
        int unusedDigits = (limbs.Length - limbCount) * Base58Pow5Digits;

        for (int j = 0; j < limbCount; j++)
        {
            uint v = limbs[limbCount - 1 - j];
            int at = unusedDigits + (Base58Pow5Digits * j);
            raw[at + 4] = (byte)(v % 58U);
            raw[at + 3] = (byte)(v / 58U % 58U);
            raw[at + 2] = (byte)(v / 3364U % 58U);
            raw[at + 1] = (byte)(v / 195112U % 58U);
            raw[at + 0] = (byte)(v / 11316496U);
        }

        int rawLeadingZeros = unusedDigits;
        while (rawLeadingZeros < raw.Length && raw[rawLeadingZeros] == 0)
        {
            rawLeadingZeros++;
        }

        return rawLeadingZeros;
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
