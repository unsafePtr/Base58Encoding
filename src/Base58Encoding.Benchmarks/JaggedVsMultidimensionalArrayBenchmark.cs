using System.Buffers.Binary;

using BenchmarkDotNet.Attributes;

namespace Base58Encoding.Benchmarks;

/// <summary>
/// Benchmark comparing jagged arrays (uint[][]) vs multidimensional arrays (uint[,]) 
/// for Base58 table lookup performance using the same Fast32 encode/decode logic
/// Verdict - on encoding both are the same, but on decoding jagged arrays are 5-10% faster.
/// </summary>
[MemoryDiagnoser]
public class JaggedVsMultidimensionalArrayBenchmark
{
    private byte[] _data = default!;
    private string _encodedBase58 = default!;

    // Jagged arrays (current implementation)
    private static readonly uint[][] JaggedEncodeTable32 = Base58BitcoinTables.EncodeTable32;
    private static readonly uint[][] JaggedDecodeTable32 = Base58BitcoinTables.DecodeTable32;

    // Multidimensional arrays (alternative implementation)
    private static readonly uint[,] MultidimensionalEncodeTable32 = ConvertToMultidimensional(Base58BitcoinTables.EncodeTable32);
    private static readonly uint[,] MultidimensionalDecodeTable32 = ConvertToMultidimensional(Base58BitcoinTables.DecodeTable32);

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[32];
        Random.Shared.NextBytes(_data);
        _encodedBase58 = SimpleBase.Base58.Bitcoin.Encode(_data);
    }

    [Benchmark(Baseline = true)]
    public string EncodeWithJaggedArray()
    {
        return EncodeBitcoin32FastJagged(_data);
    }

    [Benchmark]
    public string EncodeWithMultidimensionalArray()
    {
        return EncodeBitcoin32FastMultidimensional(_data);
    }

    [Benchmark]
    public byte[] DecodeWithJaggedArray()
    {
        return DecodeBitcoin32FastJagged(_encodedBase58)!;
    }

    [Benchmark]
    public byte[] DecodeWithMultidimensionalArray()
    {
        return DecodeBitcoin32FastMultidimensional(_encodedBase58)!;
    }

    private static uint[,] ConvertToMultidimensional(uint[][] jaggedArray)
    {
        int rows = jaggedArray.Length;
        int cols = jaggedArray[0].Length;
        var result = new uint[rows, cols];

        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                result[i, j] = jaggedArray[i][j];
            }
        }

        return result;
    }

    private static string EncodeBitcoin32FastJagged(ReadOnlySpan<byte> data)
    {
        // Count leading zeros
        int inLeadingZeros = Base58.CountLeadingZeros(data);

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

        // Convert to intermediate format (base 58^5) using JAGGED ARRAY
        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz32];
        intermediate.Clear();

        for (int i = 0; i < Base58BitcoinTables.BinarySz32; i++)
        {
            for (int j = 0; j < Base58BitcoinTables.IntermediateSz32 - 1; j++)
            {
                intermediate[j + 1] += (ulong)binary[i] * JaggedEncodeTable32[i][j];
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
        }

        // Count leading zeros in raw output
        int rawLeadingZeros = 0;
        for (; rawLeadingZeros < Base58BitcoinTables.Raw58Sz32; rawLeadingZeros++)
        {
            if (rawBase58[rawLeadingZeros] != 0) break;
        }

        // Calculate skip and final length
        int skip = rawLeadingZeros - inLeadingZeros;
        int outputLength = Base58BitcoinTables.Raw58Sz32 - skip;
        var state = new Base58.EncodeFastState(rawBase58, inLeadingZeros, rawLeadingZeros, outputLength);
        return string.Create(outputLength, state, static (span, state) =>
        {
            if (state.InLeadingZeros > 0)
            {
                span[..state.InLeadingZeros].Fill('1');
            }

            var bitcoinChars = Base58BitcoinTables.BitcoinChars;
            for (int i = 0; i < state.OutputLength - state.InLeadingZeros; i++)
            {
                byte digit = state.RawBase58[state.RawLeadingZeros + i];
                span[state.InLeadingZeros + i] = bitcoinChars[digit];
            }
        });
    }

    private static string EncodeBitcoin32FastMultidimensional(ReadOnlySpan<byte> data)
    {
        // Count leading zeros
        int inLeadingZeros = Base58.CountLeadingZeros(data);

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

        // Convert to intermediate format (base 58^5) using MULTIDIMENSIONAL ARRAY
        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz32];
        intermediate.Clear();

        for (int i = 0; i < Base58BitcoinTables.BinarySz32; i++)
        {
            for (int j = 0; j < Base58BitcoinTables.IntermediateSz32 - 1; j++)
            {
                intermediate[j + 1] += (ulong)binary[i] * MultidimensionalEncodeTable32[i, j];
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
        }

        // Count leading zeros in raw output
        int rawLeadingZeros = 0;
        for (; rawLeadingZeros < Base58BitcoinTables.Raw58Sz32; rawLeadingZeros++)
        {
            if (rawBase58[rawLeadingZeros] != 0) break;
        }

        // Calculate skip and final length
        int skip = rawLeadingZeros - inLeadingZeros;
        int outputLength = Base58BitcoinTables.Raw58Sz32 - skip;

        var state = new Base58.EncodeFastState(rawBase58, inLeadingZeros, rawLeadingZeros, outputLength);
        return string.Create(outputLength, state, static (span, state) =>
        {
            if (state.InLeadingZeros > 0)
            {
                span[..state.InLeadingZeros].Fill('1');
            }

            var bitcoinChars = Base58BitcoinTables.BitcoinChars;
            for (int i = 0; i < state.OutputLength - state.InLeadingZeros; i++)
            {
                byte digit = state.RawBase58[state.RawLeadingZeros + i];
                span[state.InLeadingZeros + i] = bitcoinChars[digit];
            }
        });
    }

    private static byte[]? DecodeBitcoin32FastJagged(string encoded)
    {
        // Early validation and length check
        if (encoded.Length > Base58BitcoinTables.Raw58Sz32) return null;

        // Validate characters and create raw array using JAGGED ARRAY lookup
        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz32];
        var bitcoinDecodeTable = Base58Alphabet.Bitcoin.DecodeTable.Span;

        int prepend0 = Base58BitcoinTables.Raw58Sz32 - encoded.Length;
        for (int j = 0; j < Base58BitcoinTables.Raw58Sz32; j++)
        {
            if (j < prepend0)
            {
                rawBase58[j] = 0;
            }
            else
            {
                char c = encoded[j - prepend0];
                if (c >= 128 || bitcoinDecodeTable[c] == 255)
                    return null;

                rawBase58[j] = bitcoinDecodeTable[c];
            }
        }

        // Convert to intermediate format
        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz32];
        for (int i = 0; i < Base58BitcoinTables.IntermediateSz32; i++)
        {
            intermediate[i] = (ulong)rawBase58[5 * i + 0] * 11316496UL +
                              (ulong)rawBase58[5 * i + 1] * 195112UL +
                              (ulong)rawBase58[5 * i + 2] * 3364UL +
                              (ulong)rawBase58[5 * i + 3] * 58UL +
                              (ulong)rawBase58[5 * i + 4] * 1UL;
        }

        // Convert to binary using JAGGED ARRAY
        Span<ulong> binary = stackalloc ulong[Base58BitcoinTables.BinarySz32];
        for (int j = 0; j < Base58BitcoinTables.BinarySz32; j++)
        {
            ulong acc = 0UL;
            for (int i = 0; i < Base58BitcoinTables.IntermediateSz32; i++)
            {
                acc += intermediate[i] * JaggedDecodeTable32[i][j];
            }
            binary[j] = acc;
        }

        // Reduce to proper uint32 values
        for (int i = Base58BitcoinTables.BinarySz32 - 1; i > 0; i--)
        {
            binary[i - 1] += binary[i] >> 32;
            binary[i] &= 0xFFFFFFFFUL;
        }

        if (binary[0] > 0xFFFFFFFFUL) return null;

        // Convert to output bytes
        var result = new byte[32];
        for (int i = 0; i < Base58BitcoinTables.BinarySz32; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(i * 4, 4), (uint)binary[i]);
        }

        // Validate leading zeros match leading '1's
        int leadingZeroCnt = 0;
        for (; leadingZeroCnt < 32; leadingZeroCnt++)
        {
            if (result[leadingZeroCnt] != 0) break;
            if (encoded.Length <= leadingZeroCnt || encoded[leadingZeroCnt] != '1') return null;
        }
        if (leadingZeroCnt < encoded.Length && encoded[leadingZeroCnt] == '1') return null;

        return result;
    }

    private static byte[]? DecodeBitcoin32FastMultidimensional(string encoded)
    {
        // Early validation and length check
        if (encoded.Length > Base58BitcoinTables.Raw58Sz32) return null;

        // Validate characters and create raw array using MULTIDIMENSIONAL ARRAY lookup
        Span<byte> rawBase58 = stackalloc byte[Base58BitcoinTables.Raw58Sz32];
        var bitcoinDecodeTable = Base58Alphabet.Bitcoin.DecodeTable.Span;

        int prepend0 = Base58BitcoinTables.Raw58Sz32 - encoded.Length;
        for (int j = 0; j < Base58BitcoinTables.Raw58Sz32; j++)
        {
            if (j < prepend0)
            {
                rawBase58[j] = 0;
            }
            else
            {
                char c = encoded[j - prepend0];
                if (c >= 128 || bitcoinDecodeTable[c] == 255)
                    return null;

                rawBase58[j] = bitcoinDecodeTable[c];
            }
        }

        // Convert to intermediate format
        Span<ulong> intermediate = stackalloc ulong[Base58BitcoinTables.IntermediateSz32];
        for (int i = 0; i < Base58BitcoinTables.IntermediateSz32; i++)
        {
            intermediate[i] = (ulong)rawBase58[5 * i + 0] * 11316496UL +
                              (ulong)rawBase58[5 * i + 1] * 195112UL +
                              (ulong)rawBase58[5 * i + 2] * 3364UL +
                              (ulong)rawBase58[5 * i + 3] * 58UL +
                              (ulong)rawBase58[5 * i + 4] * 1UL;
        }

        // Convert to binary using MULTIDIMENSIONAL ARRAY
        Span<ulong> binary = stackalloc ulong[Base58BitcoinTables.BinarySz32];
        for (int j = 0; j < Base58BitcoinTables.BinarySz32; j++)
        {
            ulong acc = 0UL;
            for (int i = 0; i < Base58BitcoinTables.IntermediateSz32; i++)
            {
                acc += intermediate[i] * MultidimensionalDecodeTable32[i, j];
            }
            binary[j] = acc;
        }

        // Reduce to proper uint32 values
        for (int i = Base58BitcoinTables.BinarySz32 - 1; i > 0; i--)
        {
            binary[i - 1] += binary[i] >> 32;
            binary[i] &= 0xFFFFFFFFUL;
        }

        if (binary[0] > 0xFFFFFFFFUL) return null;

        // Convert to output bytes
        var result = new byte[32];
        for (int i = 0; i < Base58BitcoinTables.BinarySz32; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(i * 4, 4), (uint)binary[i]);
        }

        // Validate leading zeros match leading '1's
        int leadingZeroCnt = 0;
        for (; leadingZeroCnt < 32; leadingZeroCnt++)
        {
            if (result[leadingZeroCnt] != 0) break;
            if (encoded.Length <= leadingZeroCnt || encoded[leadingZeroCnt] != '1') return null;
        }
        if (leadingZeroCnt < encoded.Length && encoded[leadingZeroCnt] == '1') return null;

        return result;
    }
}
