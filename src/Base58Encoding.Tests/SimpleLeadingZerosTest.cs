using System.Runtime.Intrinsics;

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

    [Theory]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(23)]
    [InlineData(31)]
    public void CountLeadingZeros_32Size_ReturnsCorrectNumber(int zerosCount)
    {
        // Arrange
        var data = new byte[32];
        data.AsSpan(0, zerosCount).Fill(0x00);
        Random.Shared.NextBytes(data.AsSpan(zerosCount));

        // Act
        var result = Base58.CountLeadingZerosSimd(data, out var processed);

        Assert.Equal(zerosCount, result);
        Assert.Equal(data.Length, processed);
    }

    [Fact]
    public void CountLeadingZeros_512Size_ReturnsCorrectNumber()
    {
        // Arrange
        var zerosCount = 123;
        var data = new byte[512];
        data.AsSpan(0, zerosCount).Fill(0x00);
        Random.Shared.NextBytes(data.AsSpan(zerosCount));
        // Act
        var result = Base58.CountLeadingZerosSimd(data, out var processed);
        Assert.Equal(zerosCount, result);
        Assert.Equal(Vector256<byte>.Count * 4, processed); // Vector256 used 4 times
    }
}
