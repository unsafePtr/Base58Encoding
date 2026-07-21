namespace Base58Encoding.Tests;

public class SimpleLeadingZerosTest
{
    [Fact]
    public void BitcoinAddress_CountLeadingZeros_MatchesManualCount()
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

        Assert.Equal(manualCount, Base58.CountLeadingZeros(decoded));
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
        data[zerosCount] = 1;

        // Act / Assert
        Assert.Equal(zerosCount, Base58.CountLeadingZeros(data));
    }

    [Fact]
    public void CountLeadingZeros_512Size_ReturnsCorrectNumber()
    {
        // Arrange
        var zerosCount = 123;
        var data = new byte[512];
        data.AsSpan(0, zerosCount).Fill(0x00);
        Random.Shared.NextBytes(data.AsSpan(zerosCount));
        data[zerosCount] = 1;

        // Act / Assert
        Assert.Equal(zerosCount, Base58.CountLeadingZeros(data));
    }

    [Fact]
    public void CountLeadingZeros_AllZeros_ReturnsLength()
    {
        var data = new byte[40];

        Assert.Equal(data.Length, Base58.CountLeadingZeros(data));
    }
}
