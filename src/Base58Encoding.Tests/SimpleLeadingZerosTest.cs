namespace Base58Encoding.Tests;

public class SimpleLeadingZerosTest
{
    [Fact]
    public void BitcoinAddress_CountLeadingZerosMultipleWays_SameResult()
    {
        var address = "1111111111111111111114oLvT2";
        var decoded = Base58.Bitcoin.Decode(address);

        // Count leading zeros manually
        int manualCount = 0;
        for (int i = 0; i < decoded.Length; i++)
        {
            if (decoded[i] != 0) break;
            manualCount++;
        }

        // Test SIMD
        int simdCount = Base58.CountLeadingZerosSimd(decoded, out int processed);
        var simdScalarCount = 0;
        if (simdCount >= processed)
        {
            int remaining = Base58.CountLeadingZerosScalar(decoded.AsSpan(simdCount));
            simdScalarCount = simdCount + remaining;
        }

        Assert.Equal(simdScalarCount, manualCount);
    }
}