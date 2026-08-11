using System.Numerics;
using System.Text;

namespace Base58Encoding.Tests;

// The generic encode path builds the value in base 58^5 and sizes its limb scratch from
// GetMaxEncodedLength. That bound has to hold at every input length, not just the ones the
// sampled fuzz happens to hit, so this walks all lengths up to 400 with the values that maximise
// and minimise the limb count. Lengths above ~186 bytes cross into the ArrayPool scratch path.
public class GenericEncodeLimbBoundsTests
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    [Fact]
    public void Encode_MatchesOracle_AtEveryLengthAndEdgeValue()
    {
        for (int length = 1; length <= 400; length++)
        {
            foreach (byte[] data in EdgeCases(length))
            {
                string oracle = OracleEncode(data);

                string ours = Base58.Bitcoin.Encode(data);
                Assert.Equal(oracle, ours);

                Span<byte> destination = new byte[Base58.GetMaxEncodedLength(length)];
                int written = Base58.Bitcoin.Encode(data, destination);
                Assert.Equal(oracle, Encoding.ASCII.GetString(destination[..written]));

                Assert.Equal(data, Base58.Bitcoin.Decode(ours));
            }
        }
    }

    // Same sweep on a non-Bitcoin alphabet, which has no fast path at all, so every length goes
    // through the generic encoder.
    [Fact]
    public void Encode_NonBitcoinAlphabet_RoundTrips_AtEveryLength()
    {
        for (int length = 1; length <= 400; length++)
        {
            foreach (byte[] data in EdgeCases(length))
            {
                string encoded = Base58.Ripple.Encode(data);
                Assert.Equal(data, Base58.Ripple.Decode(encoded));
                Assert.Equal(OracleEncode(data).Length, encoded.Length);
            }
        }
    }

    // The values that stress the limb bound: the largest number of that byte length, the smallest,
    // and the leading-zero forms that shift where the significant digits start.
    private static IEnumerable<byte[]> EdgeCases(int length)
    {
        byte[] max = new byte[length];
        max.AsSpan().Fill(0xFF);
        yield return max;

        byte[] high = new byte[length];
        high[0] = 0x80;
        yield return high;

        byte[] low = new byte[length];
        low[0] = 0x01;
        yield return low;

        yield return new byte[length];

        if (length > 1)
        {
            byte[] leadingZero = new byte[length];
            leadingZero.AsSpan(1).Fill(0xFF);
            yield return leadingZero;
        }

        byte[] random = new byte[length];
        new Random(length).NextBytes(random);
        random[0] |= 1;
        yield return random;
    }

    // The literal definition of Base58: big-endian integer, repeated divide-by-58, one '1' per
    // leading zero byte.
    private static string OracleEncode(byte[] data)
    {
        int zeros = 0;
        while (zeros < data.Length && data[zeros] == 0)
        {
            zeros++;
        }

        var num = new BigInteger(data, isUnsigned: true, isBigEndian: true);
        var sb = new StringBuilder();
        while (num > 0)
        {
            num = BigInteger.DivRem(num, 58, out BigInteger rem);
            sb.Insert(0, Alphabet[(int)rem]);
        }

        for (int i = 0; i < zeros; i++)
        {
            sb.Insert(0, '1');
        }

        return sb.ToString();
    }
}
