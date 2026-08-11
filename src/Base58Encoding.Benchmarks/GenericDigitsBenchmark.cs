using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

using BenchmarkDotNet.Attributes;

namespace Base58Encoding.Benchmarks;

// Generic (non-32/64-byte) encode path, A/B in one process.
//
// ByteDigits  = the production algorithm: one input byte at a time into base-58 byte digits,
//               a DivRem per digit per byte, so inner iterations grow as O(n^2) in the digit count.
// Limb58Pow5  = four input bytes at a time into uint limbs of base 58^5 with ulong intermediates,
//               then one expansion pass turning each limb into five base-58 digits.
//
// Both arms run the whole encode (leading-zero scan, digit computation, alphabet emit) so the
// comparison reflects the real path, not just the inner loop.
[MemoryDiagnoser]
[HideColumns("RatioSD")]
public class GenericDigitsBenchmark
{
    private const int Base = 58;
    private const uint Base58Pow5 = 656356768U; // 58^5, the largest power of 58 that fits in a uint

    // 25 = Bitcoin address, 34 = IPFS hash, 69 = Monero address, 128 = a large-but-stackalloc case.
    [Params(25, 34, 69, 128)]
    public int Size { get; set; }

    private byte[] _data = default!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        _data = new byte[Size];
        rng.NextBytes(_data);
        _data[0] |= 1; // keep the input off the leading-zero path so both arms do full work

        byte[] expected = Encoding.ASCII.GetBytes(Base58.Bitcoin.Encode(_data));

        Span<byte> scratch = stackalloc byte[256];
        int n = EncodeByteDigits(_data, scratch);
        if (!scratch[..n].SequenceEqual(expected))
        {
            throw new InvalidOperationException($"ByteDigits does not match production Encode for size {Size}");
        }

        n = EncodeLimb58Pow5(_data, scratch);
        if (!scratch[..n].SequenceEqual(expected))
        {
            throw new InvalidOperationException($"Limb58Pow5 does not match production Encode for size {Size}");
        }
    }

    [Benchmark(Baseline = true)]
    public int ByteDigits()
    {
        Span<byte> dest = stackalloc byte[256];
        return EncodeByteDigits(_data, dest);
    }

    [Benchmark]
    public int Limb58Pow5()
    {
        Span<byte> dest = stackalloc byte[256];
        return EncodeLimb58Pow5(_data, dest);
    }

    // ---- arm A: current production algorithm -------------------------------------------------

    [SkipLocalsInit]
    private static int EncodeByteDigits(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        int leadingZeros = Base58.CountLeadingZeros(data);
        ReadOnlySpan<byte> inputSpan = data[leadingZeros..];
        int size = Base58.GetMaxEncodedLength(inputSpan.Length);

        Span<byte> digits = stackalloc byte[size];
        int digitCount = ComputeGenericDigits(inputSpan, digits);

        if (leadingZeros > 0)
        {
            destination[..leadingZeros].Fill((byte)'1');
        }

        int index = leadingZeros;
        ReadOnlySpan<byte> alphabet = BitcoinAlphabet.Characters;
        for (int i = digitCount - 1; i >= 0; i--)
        {
            destination[index++] = alphabet[digits[i]];
        }

        return leadingZeros + digitCount;
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

    // ---- arm B: base-58^5 limbs ---------------------------------------------------------------

    [SkipLocalsInit]
    private static int EncodeLimb58Pow5(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        int leadingZeros = Base58.CountLeadingZeros(data);
        ReadOnlySpan<byte> inputSpan = data[leadingZeros..];

        // One limb carries five base-58 digits, so the limb bound is the digit bound over five.
        int limbCapacity = (Base58.GetMaxEncodedLength(inputSpan.Length) + 4) / 5;

        Span<uint> limbs = stackalloc uint[limbCapacity];
        Span<byte> raw = stackalloc byte[limbCapacity * 5];

        int rawLeadingZeros = ComputeLimbDigits(inputSpan, limbs, raw);
        int digitCount = raw.Length - rawLeadingZeros;

        if (leadingZeros > 0)
        {
            destination[..leadingZeros].Fill((byte)'1');
        }

        int index = leadingZeros;
        ReadOnlySpan<byte> alphabet = BitcoinAlphabet.Characters;
        for (int i = rawLeadingZeros; i < raw.Length; i++)
        {
            destination[index++] = alphabet[raw[i]];
        }

        return leadingZeros + digitCount;
    }

    // Builds the value in base 58^5, then expands each limb into five base-58 digits written
    // most-significant first. Returns the number of leading zero digits in `raw`.
    private static int ComputeLimbDigits(ReadOnlySpan<byte> inputSpan, Span<uint> limbs, Span<byte> raw)
    {
        int limbCount = 1;
        limbs[0] = 0;

        // A leading partial word of 1-3 bytes is below 2^24, hence already a valid single limb.
        int head = inputSpan.Length & 3;
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
            // value = value * 2^32 + word. carry stays below 2^32, so cur stays below 58^5 * 2^32,
            // which is 2.82e18 and fits a ulong.
            ulong carry = BinaryPrimitives.ReadUInt32BigEndian(inputSpan.Slice(offset, sizeof(uint)));

            for (int i = 0; i < limbCount; i++)
            {
                ulong cur = ((ulong)limbs[i] << 32) + carry;
                ulong q = cur / Base58Pow5;
                limbs[i] = (uint)(cur - (q * Base58Pow5));
                carry = q;
            }

            // carry < 2^32 < 7 * 58^5, so this runs at most twice.
            while (carry != 0)
            {
                ulong q = carry / Base58Pow5;
                limbs[limbCount++] = (uint)(carry - (q * Base58Pow5));
                carry = q;
            }
        }

        // The digits are right-aligned in `raw`: the limb bound overshoots the true limb count by
        // at most a limb or two, and the unwritten prefix is exactly the leading zeros we skip.
        int unusedDigits = (limbs.Length - limbCount) * 5;

        for (int j = 0; j < limbCount; j++)
        {
            uint v = limbs[limbCount - 1 - j];
            int at = unusedDigits + (5 * j);
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
}
