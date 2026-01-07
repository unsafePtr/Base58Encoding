using System.Buffers.Binary;
using System.Diagnostics;

namespace Base58Encoding;

public partial class Base58
{
    /// <summary>
    /// Encode byte array to Base58 string
    /// </summary>
    /// <param name="data">Bytes to encode</param>
    /// <returns>Base58 encoded string</returns>
    public string Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return string.Empty;

        // Hot path for Bitcoin alphabet + common sizes
        if (ReferenceEquals(this, _bitcoin.Value))
        {
            return data.Length switch
            {
                32 => EncodeBitcoin32Fast(data),
                64 => EncodeBitcoin64Fast(data),
                _ => EncodeGeneric(data)
            };
        }

        // Fallback for other alphabets
        return EncodeGeneric(data);
    }

    /// <summary>
    /// Encode byte array to Base58 string using generic algorithm
    /// </summary>
    /// <param name="data">Bytes to encode</param>
    /// <returns>Base58 encoded string</returns>
    internal string EncodeGeneric(ReadOnlySpan<byte> data)
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
        return string.Create(resultSize, new EncodeGenericFinalString(_characters.Span, digits, _firstCharacter, leadingZeros, digitCount), static (span, state) =>
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

    internal static string EncodeBitcoin32Fast(ReadOnlySpan<byte> data)
    {
        // Count leading zeros (needed for final output)
        int inLeadingZeros = CountLeadingZeros(data);

        if (inLeadingZeros == data.Length)
        {
            return new string('1', inLeadingZeros);
        }

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

        // Convert intermediate form to raw base58 digits
        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz32];
        for (int i = 0; i < Base58BitcoinTables.IntermediateSz32; i++)
        {
            uint v = (uint)intermediate[i];

            rawBase58[5 * i + 4] = (byte)((v / 1U) % 58U);
            rawBase58[5 * i + 3] = (byte)((v / 58U) % 58U);
            rawBase58[5 * i + 2] = (byte)((v / 3364U) % 58U);
            rawBase58[5 * i + 1] = (byte)((v / 195112U) % 58U);
            rawBase58[5 * i + 0] = (byte)(v / 11316496U);

            // Continue processing all values
        }

        // Count leading zeros in raw output
        int rawLeadingZeros = 0;
        for (; rawLeadingZeros < Base58BitcoinTables.Raw58Sz32; rawLeadingZeros++)
        {
            if (rawBase58[rawLeadingZeros] != 0) break;
        }

        // Calculate skip and final length (match Firedancer exactly)
        int skip = rawLeadingZeros - inLeadingZeros;
        Debug.Assert(skip >= 0, "rawLeadingZeros should always be >= inLeadingZeros by Base58 math");
        int outputLength = Base58BitcoinTables.Raw58Sz32 - skip;

        // Create state for string.Create
        var state = new EncodeFastState(rawBase58, inLeadingZeros, rawLeadingZeros, outputLength);

        return string.Create(outputLength, state, static (span, state) =>
        {
            // Fill leading '1's for input leading zeros
            if (state.InLeadingZeros > 0)
            {
                span[..state.InLeadingZeros].Fill('1');
            }

            // Convert remaining raw base58 digits to characters
            // Read from rawLeadingZeros onwards (where the actual digits are)
            var bitcoinChars = Base58BitcoinTables.BitcoinChars;
            for (int i = 0; i < state.OutputLength - state.InLeadingZeros; i++)
            {
                byte digit = state.RawBase58[state.RawLeadingZeros + i];
                Debug.Assert(digit < 58, $"Base58 digit should always be < 58, got {digit}");
                span[state.InLeadingZeros + i] = bitcoinChars[digit];
            }
        });
    }

    private static string EncodeBitcoin64Fast(ReadOnlySpan<byte> data)
    {
        // Count leading zeros (needed for final output)
        int inLeadingZeros = CountLeadingZeros(data);

        if (inLeadingZeros == data.Length)
        {
            return new string('1', inLeadingZeros);
        }

        // Convert 64 bytes to 16 uint32 limbs (big-endian)
        Span<uint> binary = stackalloc uint[Base58BitcoinTables.BinarySz64];
        for (int i = 0; i < Base58BitcoinTables.BinarySz64; i++)
        {
            int offset = i * sizeof(uint);
            binary[i] = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, sizeof(uint)));
        }

        // Convert to intermediate format (base 58^5)
        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz64];
        intermediate.Clear();

        // Matrix multiplication: intermediate = binary * EncodeTable64
        // For 64-byte, we need to handle potential overflow like Firedancer does
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

        // Convert intermediate form to raw base58 digits
        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz64];
        for (int i = 0; i < Base58BitcoinTables.IntermediateSz64; i++)
        {
            uint v = (uint)intermediate[i];
            rawBase58[5 * i + 4] = (byte)((v / 1U) % 58U);
            rawBase58[5 * i + 3] = (byte)((v / 58U) % 58U);
            rawBase58[5 * i + 2] = (byte)((v / 3364U) % 58U);
            rawBase58[5 * i + 1] = (byte)((v / 195112U) % 58U);
            rawBase58[5 * i + 0] = (byte)(v / 11316496U);

            // Defensive check - ensure all values are valid Base58 digits
            if (rawBase58[5 * i + 0] >= 58 || rawBase58[5 * i + 1] >= 58 ||
                rawBase58[5 * i + 2] >= 58 || rawBase58[5 * i + 3] >= 58 || rawBase58[5 * i + 4] >= 58)
            {
                throw new InvalidOperationException($"Invalid base58 digit generated at position {i}: {rawBase58[5 * i + 0]}, {rawBase58[5 * i + 1]}, {rawBase58[5 * i + 2]}, {rawBase58[5 * i + 3]}, {rawBase58[5 * i + 4]}");
            }
        }

        // Count leading zeros in raw output
        int rawLeadingZeros = 0;
        for (; rawLeadingZeros < Base58BitcoinTables.Raw58Sz64; rawLeadingZeros++)
        {
            if (rawBase58[rawLeadingZeros] != 0) break;
        }

        // Calculate skip and final length
        int skip = rawLeadingZeros - inLeadingZeros;
        Debug.Assert(skip >= 0, "rawLeadingZeros should always be >= inLeadingZeros by Base58 math");
        int outputLength = Base58BitcoinTables.Raw58Sz64 - skip;

        // Create state for string.Create
        var state = new EncodeFastState(rawBase58, inLeadingZeros, rawLeadingZeros, outputLength);

        return string.Create(outputLength, state, static (span, state) =>
        {
            // Fill leading '1's for input leading zeros
            if (state.InLeadingZeros > 0)
            {
                span[..state.InLeadingZeros].Fill('1');
            }

            // Convert remaining raw base58 digits to characters
            // Read from rawLeadingZeros onwards (where the actual digits are)
            var bitcoinChars = Base58BitcoinTables.BitcoinChars;
            for (int i = 0; i < state.OutputLength - state.InLeadingZeros; i++)
            {
                byte digit = state.RawBase58[state.RawLeadingZeros + i];
                Debug.Assert(digit < 58, $"Base58 digit should always be < 58, got {digit}");
                span[state.InLeadingZeros + i] = bitcoinChars[digit];
            }
        });
    }

    private readonly ref struct EncodeFastState
    {
        public readonly ReadOnlySpan<byte> RawBase58;
        public readonly int InLeadingZeros;
        public readonly int RawLeadingZeros;
        public readonly int OutputLength;

        public EncodeFastState(ReadOnlySpan<byte> rawBase58, int inLeadingZeros, int rawLeadingZeros, int outputLength)
        {
            RawBase58 = rawBase58;
            InLeadingZeros = inLeadingZeros;
            RawLeadingZeros = rawLeadingZeros;
            OutputLength = outputLength;
        }
    }

    private readonly ref struct EncodeGenericFinalString
    {
        public readonly ReadOnlySpan<char> Alphabet;
        public readonly ReadOnlySpan<byte> Digits;
        public readonly char FirstCharacter;
        public readonly int LeadingZeroes;
        public readonly int DigitCount;

        public EncodeGenericFinalString(ReadOnlySpan<char> alphabet, ReadOnlySpan<byte> digits, char firstCharacter, int leadingZeroes, int digitCount)
        {
            Alphabet = alphabet;
            Digits = digits;
            FirstCharacter = firstCharacter;
            LeadingZeroes = leadingZeroes;
            DigitCount = digitCount;
        }
    }
}
