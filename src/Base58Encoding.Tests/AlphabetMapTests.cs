namespace Base58Encoding.Tests;

// VectorMath.MapBitcoinAlphabet replaces the per-digit table load with arithmetic: the Bitcoin
// alphabet is six runs of consecutive ASCII, so the character is the digit plus an offset selected
// by five comparisons. That trades a table for five breakpoints, and a breakpoint off by one would
// corrupt exactly one alphabet run -- so these walk every digit through every lane position and
// every vector-width boundary, against the table the arithmetic replaced.
public class AlphabetMapTests
{
    [Fact]
    public void MapBitcoinAlphabet_MatchesTableLookup_ForEveryDigit()
    {
        byte[] digits = new byte[58];
        for (int i = 0; i < 58; i++)
        {
            digits[i] = (byte)i;
        }

        byte[] mapped = new byte[58];
        VectorMath.MapBitcoinAlphabet(digits.AsSpan(), mapped.AsSpan());

        Assert.Equal("123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz", System.Text.Encoding.ASCII.GetString(mapped));
    }

    // Lengths 0..100 cross both vector widths and the scalar tail (a 32-byte encode emits 44
    // digits, a 64-byte one 88), and rotating the starting digit walks every value through every
    // lane position.
    [Fact]
    public void MapBitcoinAlphabet_MatchesTableLookup_AtEveryLengthAndLaneOffset()
    {
        ReadOnlySpan<byte> alphabet = BitcoinAlphabet.Characters;

        for (int length = 0; length <= 100; length++)
        {
            byte[] digits = new byte[length];
            byte[] expected = new byte[length];
            byte[] actualBytes = new byte[length];
            char[] actualChars = new char[length];

            for (int start = 0; start < 58; start++)
            {
                for (int i = 0; i < length; i++)
                {
                    digits[i] = (byte)((start + i) % 58);
                    expected[i] = alphabet[digits[i]];
                }

                VectorMath.MapBitcoinAlphabet(digits.AsSpan(), actualBytes.AsSpan());
                Assert.Equal(expected, actualBytes);

                VectorMath.MapBitcoinAlphabet(digits.AsSpan(), actualChars.AsSpan());
                for (int i = 0; i < length; i++)
                {
                    Assert.Equal((char)expected[i], actualChars[i]);
                }
            }
        }
    }

    // The map must not write past the digits it was given: the destination is a slice of the
    // caller's buffer and the vector store is 32 bytes wide.
    [Fact]
    public void MapBitcoinAlphabet_DoesNotWritePastTheDigits()
    {
        for (int length = 1; length <= 96; length++)
        {
            byte[] digits = new byte[length];
            for (int i = 0; i < length; i++)
            {
                digits[i] = (byte)(i % 58);
            }

            byte[] destination = new byte[length + 32];
            destination.AsSpan().Fill(0xCC);

            VectorMath.MapBitcoinAlphabet(digits.AsSpan(), destination.AsSpan(0, length));

            for (int i = length; i < destination.Length; i++)
            {
                Assert.Equal(0xCC, destination[i]);
            }
        }
    }
}
