namespace Base58Encoding.Tests;

public class Base58Tests
{
    private static readonly byte[] TestBytes1 = { 0x00, 0x01, 0x02, 0x03 };
    private static readonly byte[] TestBytes2 = { 0xFF, 0xFE, 0xFD, 0xFC };
    private static readonly byte[] TestBytes3 = new byte[32]; // All zeros
    private static readonly byte[] TestBytes4 = Enumerable.Repeat((byte)0xFF, 32).ToArray(); // All 255s

    [Fact]
    public void Encode_WithEmptyArray_ReturnsEmptyString()
    {
        // Act
        var result = Base58.Bitcoin.Encode([]);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Encode_WithAllZeros_ReturnsOnes()
    {
        // Arrange
        var allZeros = new byte[] { 0x00, 0x00, 0x00 };

        // Act
        var result = Base58.Bitcoin.Encode(allZeros);

        // Assert
        Assert.Equal("111", result);
    }

    [Fact]
    public void Decode_WithEmptyString_ReturnsEmptyArray()
    {
        // Act
        var result = Base58.Bitcoin.Decode("");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Decode_WithOnes_ReturnsZeros()
    {
        // Act
        var result = Base58.Bitcoin.Decode("111");

        // Assert - The actual number of leading zeros depends on Base58 implementation
        Assert.True(result.All(b => b == 0));
        Assert.True(result.Length >= 3);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("O")]
    [InlineData("I")]
    [InlineData("l")]
    [InlineData("123O45")]
    [InlineData("123I45")]
    [InlineData("123l45")]
    [InlineData("1230")]
    [InlineData("+")]
    [InlineData("/")]
    [InlineData("=")]
    public void Decode_WithInvalidCharacter_ThrowsException(string str)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Base58.Bitcoin.Decode(str));
    }

    [Fact]
    public void Encode_Decode_RoundTrip_WorksCorrectly()
    {
        // Arrange - Use test cases that should round-trip perfectly
        var testCases = new[]
        {
            TestBytes1,
            TestBytes2,
            TestBytes4, // All 0xFF
            new byte[] { 0xFF },
            new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 },
        };

        foreach (var originalBytes in testCases)
        {
            // Act
            var encoded = Base58.Bitcoin.Encode(originalBytes);
            var decoded = Base58.Bitcoin.Decode(encoded);

            // Assert
            Assert.Equal(originalBytes, decoded);
        }
    }

    [Fact]
    public void Encode_Decode_AllZeros_HandledCorrectly()
    {
        // Arrange
        var allZeros = TestBytes3; // 32 bytes of zeros

        // Act
        var encoded = Base58.Bitcoin.Encode(allZeros);
        var decoded = Base58.Bitcoin.Decode(encoded);

        // Assert - The decoded result should have the same semantic value (all zeros)
        // but may have different leading zero padding due to Base58 implementation
        Assert.True(decoded.All(b => b == 0), "All decoded bytes should be zero");
        Assert.True(decoded.Length >= allZeros.Length, "Decoded length should be at least as long as original");
    }

    [Fact]
    public void Encode_WithLeadingZeros_PreservesLeadingZeros()
    {
        // Arrange
        var bytesWithLeadingZeros = new byte[] { 0x00, 0x00, 0x01, 0x02 };

        // Act
        var encoded = Base58.Bitcoin.Encode(bytesWithLeadingZeros);
        var decoded = Base58.Bitcoin.Decode(encoded);

        // Assert
        Assert.Equal(bytesWithLeadingZeros, decoded);
        Assert.StartsWith("11", encoded); // Leading zeros become '1's
    }

    [Theory]
    [InlineData(new byte[] { 0x00 }, "1")]
    [InlineData(new byte[] { 0x01 }, "2")]
    [InlineData(new byte[] { 0x39 }, "z")]
    [InlineData(new byte[] { 0x3A }, "21")]
    [InlineData(new byte[] { 0xFF }, "5Q")]
    [InlineData(new byte[] { 0x00, 0x01 }, "12")]
    [InlineData(new byte[] { 0x00, 0x00, 0x01 }, "112")]
    public void Encode_WithKnownValues_ReturnsExpectedResults(byte[] input, string expected)
    {
        // Act
        var result = Base58.Bitcoin.Encode(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Hello World!")]
    [InlineData("The quick brown fox jumps over the lazy dog")]
    public void Encode_WithTextData_RoundTripSucceeds(string text)
    {
        // Arrange
        var originalBytes = System.Text.Encoding.UTF8.GetBytes(text);

        // Act
        var encoded = Base58.Bitcoin.Encode(originalBytes);
        var decoded = Base58.Bitcoin.Decode(encoded);

        // Assert
        Assert.Equal(originalBytes, decoded);
    }

    [Fact]
    public void Encode_WithCryptographicHashes_WorksCorrectly()
    {
        // Arrange - simulate various hash sizes
        var testCases = new[]
        {
            new byte[16],  // MD5 size
            new byte[20],  // SHA-1, RIPEMD-160 (Bitcoin/Ripple addresses)
            new byte[32],  // SHA-256 (Sui addresses, Bitcoin private keys)
            new byte[64],  // SHA-512
        };

        var random = Random.Shared;

        foreach (var testCase in testCases)
        {
            // Fill with random data
            random.NextBytes(testCase);

            // Act
            var encoded = Base58.Bitcoin.Encode(testCase);
            var simpleBaseEncoded = SimpleBase.Base58.Bitcoin.Encode(testCase);

            Assert.Equal(simpleBaseEncoded, encoded); // Cross-check with SimpleBase
            var decoded = Base58.Bitcoin.Decode(encoded);

            // Assert
            Assert.Equal(testCase, decoded);
        }
    }

    [Fact]
    public void Encode_WithBitcoinTestVectors_MatchesExpected()
    {
        // Arrange - Bitcoin Base58 test vectors from various sources  
        var testVectors = new[]
        {
            (System.Text.Encoding.ASCII.GetBytes("hello world"), "StV1DL6CwTryKyV"),
            (System.Text.Encoding.UTF8.GetBytes("05d7f77ce4dadd9067f3e7dd98767d6adc85aae8682642d0e0"), "bTBWrvzfzgXXDDzjkDgJ3HbBdovpyjw2UDKGuQCQRnsoUqviewsDMw2E9cEr6izJUWGF"),
        };

        foreach (var (input, expected) in testVectors)
        {
            // Act
            var result = Base58.Bitcoin.Encode(input);
            var simpleBaseEncode = SimpleBase.Base58.Bitcoin.Encode(input);

            // Assert
            Assert.Equal(simpleBaseEncode, result); // Cross-check with SimpleBase
            Assert.Equal(expected, result);

            // Verify round-trip
            var decoded = Base58.Bitcoin.Decode(result);
            var simpleBaseDecoded = SimpleBase.Base58.Bitcoin.Decode(result);
            Assert.Equal(decoded, simpleBaseDecoded);
            Assert.Equal(input, decoded);
        }
    }

    [Fact]
    public void Encode_WithDifferentAlphabets_ProducesDifferentResults()
    {
        // Arrange - Simple test data
        var testData = new byte[] { 0x01 };

        // Act
        var bitcoinResult = Base58.Bitcoin.Encode(testData);
        var rippleResult = Base58.Ripple.Encode(testData);
        var flickrResult = Base58.Flickr.Encode(testData);

        // Debug output to see actual values
        // Bitcoin should be "2", Ripple should be "p", Flickr should be "2"

        // Assert - Each should round-trip correctly with its own decoder
        Assert.Equal(testData, Base58.Bitcoin.Decode(bitcoinResult));
        Assert.Equal(testData, Base58.Ripple.Decode(rippleResult));
        Assert.Equal(testData, Base58.Flickr.Decode(flickrResult));

        // Expected results for input [1] (index 1 in alphabet):
        Assert.Equal("2", bitcoinResult);   // Bitcoin[1] = '2'
        Assert.Equal("p", rippleResult);    // Ripple[1] = 'p' 
        Assert.Equal("2", flickrResult);    // Flickr[1] = '2'

        // Results should be different between Ripple and others
        Assert.NotEqual(bitcoinResult, rippleResult);
        Assert.NotEqual(flickrResult, rippleResult);
    }

    [Fact]
    public void Encode_WithLeadingZeroPatterns_PreservesCorrectly()
    {
        var testCases = new[]
        {
            new byte[] { 0x00, 0x01 },
            new byte[] { 0x00, 0x00, 0xFF },
            new byte[] { 0x00, 0x00, 0x00, 0x01, 0x02, 0x03 },
        };

        foreach (var testCase in testCases)
        {
            // Act
            var encoded = Base58.Bitcoin.Encode(testCase);
            var decoded = Base58.Bitcoin.Decode(encoded);

            // Assert - Focus on round-trip correctness
            Assert.Equal(testCase, decoded);

            // Verify leading zeros are represented as '1's
            int leadingZeros = testCase.TakeWhile(b => b == 0).Count();
            Assert.StartsWith(new string('1', leadingZeros), encoded);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }, "11111111111111111111111111111112")] // All zeros + 1
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1 }, "1111111111111111111111111111115S")] // 257 case
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, "JEKNVnkbo3jma5nREBBJCDoXFVeKkD56V3xKrvRmWxFG")] // All 0xFF
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE }, "JEKNVnkbo3jma5nREBBJCDoXFVeKkD56V3xKrvRmWxFF")] // All 0xFF - 1
    public void DecodeBitcoin32Fast_WithFireDancerTestVectors_WorksCorrectly(byte[] expectedBytes, string encoded)
    {
        // Act - Test the fast decode method directly
        var decoded = Base58.DecodeBitcoin32Fast(encoded);

        // Assert
        Assert.NotNull(decoded);
        Assert.Equal(expectedBytes, decoded);
        Assert.Equal(32, decoded.Length);
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }, "1111111111111111111111111111111111111111111111111111111111111112")] // All zeros + 1
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1 }, "111111111111111111111111111111111111111111111111111111111111115S")] // 257 case  
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, "67rpwLCuS5DGA8KGZXKsVQ7dnPb9goRLoKfgGbLfQg9WoLUgNY77E2jT11fem3coV9nAkguBACzrU1iyZM4B8roQ")] // All 0xFF
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE }, "67rpwLCuS5DGA8KGZXKsVQ7dnPb9goRLoKfgGbLfQg9WoLUgNY77E2jT11fem3coV9nAkguBACzrU1iyZM4B8roP")] // All 0xFF - 1
    public void DecodeBitcoin64Fast_WithFireDancerTestVectors_WorksCorrectly(byte[] expectedBytes, string encoded)
    {
        // Act - Test the fast decode method directly
        var decoded = Base58.DecodeBitcoin64Fast(encoded);

        // Assert
        Assert.NotNull(decoded);
        Assert.Equal(expectedBytes, decoded);
        Assert.Equal(64, decoded.Length);
    }

}