namespace Base58Encoding.Tests;

public class Base58DecodeFast
{
    [Fact]
    public void Decode32Fast_WithValidInput_MatchesGeneric()
    {
        for (int i = 0; i < 100; i++)
        {
            // Arrange
            var testData = new byte[32];
            Random.Shared.NextBytes(testData);

            var encoded = Base58.Bitcoin.Encode(testData);

            // Act
            var fastDecoded = Base58.Bitcoin.Decode(encoded);
            var genericDecoded = Base58.Bitcoin.DecodeGeneric(encoded);

            // Assert
            Assert.Equal(genericDecoded, fastDecoded);
            Assert.Equal(testData, fastDecoded);
        }
    }

    [Fact]
    public void Decode64Fast_WithValidInput_MatchesGeneric()
    {
        var random = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            // Arrange
            var testData = new byte[64];
            random.NextBytes(testData);

            var encoded = Base58.Bitcoin.Encode(testData);

            // Act
            var fastDecoded = Base58.Bitcoin.Decode(encoded);
            var genericDecoded = Base58.Bitcoin.DecodeGeneric(encoded);

            // Assert
            Assert.Equal(genericDecoded, fastDecoded);
            Assert.Equal(testData, fastDecoded);
        }
    }

    [Fact]
    public void Decode32Fast_WithAllZeros_ReturnsNull()
    {
        // Arrange
        var allZeros = new byte[32];
        var encoded = SimpleBase.Base58.Bitcoin.Encode(allZeros);

        // Act
        var decoded = Base58.DecodeBitcoin64Fast(encoded);

        // Assert
        Assert.Null(decoded);
    }

    [Fact]
    public void Decode64Fast_WithAllZeros_WorksCorrectly()
    {
        // Arrange
        var allZeros = new byte[64];
        var encoded = SimpleBase.Base58.Bitcoin.Encode(allZeros);

        // Act
        var decoded = Base58.DecodeBitcoin64Fast(encoded);

        // Assert
        Assert.Equal(allZeros, decoded);
        Assert.Equal(new string('1', 64), encoded);
    }

    [Theory]
    [InlineData("invalid0chars")] // Invalid character '0'
    public void Decode32Fast_WithInvalidInput_ReturnsNull(string input)
    {
        Assert.Null(Base58.DecodeBitcoin64Fast(input));
    }

    [Fact]
    public void Decode32Fast_WithLeadingOnes_HandlesCorrectly()
    {
        // Arrange - 28 leading zeros + some data
        var testData = new byte[32];
        testData[28] = 0xAB;
        testData[29] = 0xCD;
        testData[30] = 0xEF;
        testData[31] = 0x12;

        var encoded = Base58.Bitcoin.Encode(testData);

        // Act
        var decoded = Base58.Bitcoin.Decode(encoded);

        // Assert
        Assert.Equal(testData, decoded);
        Assert.StartsWith(new string('1', 28), encoded);
    }

    [Fact]
    public void Decode64Fast_WithLeadingOnes_HandlesCorrectly()
    {
        // Arrange - 32 leading zeros + data
        var testData = new byte[64];
        for (int i = 32; i < 64; i++)
        {
            testData[i] = (byte)(i - 32);
        }

        var encoded = Base58.Bitcoin.Encode(testData);

        // Act
        var decoded = Base58.DecodeBitcoin64Fast(encoded);

        // Assert
        Assert.Equal(testData, decoded);
        Assert.StartsWith(new string('1', 32), encoded);
    }

    [Fact]
    public void Decode32Fast_WithKnownTestVectors_WorksCorrectly()
    {
        // Arrange - Create known 32-byte inputs and their expected Base58 outputs
        var testCases = new[]
        {
            new byte[32], // All zeros
            Enumerable.Repeat((byte)0xFF, 32).ToArray(), // All 255s
        };

        foreach (var testCase in testCases)
        {
            // Act
            var encoded = SimpleBase.Base58.Bitcoin.Encode(testCase);
            var decoded = Base58.DecodeBitcoin32Fast(encoded);

            var genericDecoded = Base58.Bitcoin.DecodeGeneric(encoded);

            // Assert
            Assert.Equal(testCase, decoded);

            // Verify round-trip with generic
            Assert.Equal(genericDecoded, decoded);
        }
    }

    [Fact]
    public void Decode64Fast_WithKnownTestVectors_WorksCorrectly()
    {
        // Arrange - Create known 64-byte inputs
        var testCases = new[]
        {
            new byte[64], // All zeros
            Enumerable.Repeat((byte)0xFF, 64).ToArray(), // All 255s
        };

        foreach (var testCase in testCases)
        {
            // Act
            var encoded = Base58.Bitcoin.Encode(testCase);
            var decoded = Base58.DecodeBitcoin64Fast(encoded);

            // Assert
            Assert.Equal(testCase, decoded);

            // Verify round-trip with generic
            var genericDecoded = Base58.Bitcoin.DecodeGeneric(encoded);
            Assert.Equal(genericDecoded, decoded);
        }
    }

    [Theory]
    [InlineData(0)]   // No leading zeros
    [InlineData(1)]   // 1 leading zero  
    [InlineData(5)]   // 5 leading zeros
    [InlineData(16)]  // Half the array
    [InlineData(31)]  // Almost all zeros
    public void Decode32Fast_WithVariousLeadingZeros_HandlesCorrectly(int leadingZeros)
    {
        // Arrange
        var testData = new byte[32];
        Random.Shared.NextBytes(testData.AsSpan(leadingZeros));

        var encoded = Base58.Bitcoin.Encode(testData);

        // Act
        var decoded = Base58.Bitcoin.Decode(encoded);

        // Assert
        Assert.Equal(testData, decoded);

        // Verify leading zeros preservation
        if (leadingZeros > 0)
        {
            Assert.StartsWith(new string('1', leadingZeros), encoded);
        }
    }

    [Theory]
    [InlineData(0)]   // No leading zeros
    [InlineData(1)]   // 1 leading zero  
    [InlineData(8)]   // 8 leading zeros
    [InlineData(32)]  // Half the array
    [InlineData(63)]  // Almost all zeros
    public void Decode64Fast_WithVariousLeadingZeros_HandlesCorrectly(int leadingZeros)
    {
        // Arrange
        var testData = new byte[64];
        Random.Shared.NextBytes(testData.AsSpan(leadingZeros));

        var encoded = Base58.Bitcoin.Encode(testData);

        // Act
        var decoded = Base58.DecodeBitcoin64Fast(encoded);

        // Assert
        Assert.Equal(testData, decoded);

        // Verify leading zeros preservation
        if (leadingZeros > 0)
        {
            Assert.StartsWith(new string('1', leadingZeros), encoded);
        }
    }

    [Fact]
    public void Decode32Fast_WithMaximumValues_WorksCorrectly()
    {
        // Arrange - Create data that will generate maximum Base58 length
        var testData = new byte[32];
        Array.Fill(testData, (byte)0xFF);

        var encoded = Base58.Bitcoin.Encode(testData);

        // Act
        var decoded = Base58.Bitcoin.Decode(encoded);

        // Assert
        Assert.Equal(testData, decoded);
        Assert.InRange(encoded.Length, 43, 44); // Maximum Base58 length for 32 bytes
    }

    [Fact]
    public void Decode64Fast_WithMaximumValues_WorksCorrectly()
    {
        // Arrange - Create data that will generate maximum Base58 length
        var testData = new byte[64];
        Array.Fill(testData, (byte)0xFF);

        var encoded = Base58.Bitcoin.Encode(testData);

        // Act
        var decoded = Base58.DecodeBitcoin64Fast(encoded);

        // Assert
        Assert.Equal(testData, decoded);
        Assert.InRange(encoded.Length, 87, 88); // Maximum Base58 length for 64 bytes
    }
}
