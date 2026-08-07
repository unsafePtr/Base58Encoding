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
    [InlineData("invalid")] // Invalid character 'l'
    [InlineData("0chars")] // Invalid character '0'
    public void Decode64Fast_WithInvalidInput_ReturnsNull(string input)
    {
        Assert.Throws<ArgumentException>(() => Base58.DecodeBitcoin64Fast(input));
    }

    [Theory]
    [InlineData("invalid")] // Invalid character 'l'
    [InlineData("0chars")] // Invalid character '0'

    public void Decode32ast_WithInvalidInput_ReturnsNull(string input)
    {
        Assert.Throws<ArgumentException>(() => Base58.DecodeBitcoin32Fast(input));
    }

    // The fast paths are chosen purely by encoded LENGTH, but the base58 length ranges are wider than
    // the byte counts they map to: 88 characters hold up to 58^88-1 (~2^515.5) while 64 bytes hold
    // 2^512-1, and 44 characters hold ~2^257.8 while 32 bytes hold 2^256-1. So a perfectly well-formed
    // string of fast-path length can still encode a value that does not fit, and the fast decoders must
    // reject it (via the `binary[0] > 0xFFFFFFFF` carry check) so the caller falls back to the generic
    // decoder. Without that check Decode would silently return the wrong byte count for roughly 93% of
    // 88-character and 72% of 44-character inputs.

    // An over-large input can be rejected by either of two guards, depending on the value:
    //   * the carry guard, `binary[0] > 0xFFFFFFFF`
    //   * the leading-zero cross-check, `outputLeadingZeros != inputLeadingOnes`
    // 2^512 exactly trips the SECOND one — its top limb is 2^32, whose low 32 bits are zero, so every
    // limb looks like a leading zero byte. To exercise the carry guard the top limb's low 32 bits must
    // be large, hence the 0xFF in the second byte below.
    [Fact]
    public void Decode64Fast_RejectsValueTooLargeFor64Bytes()
    {
        var largestValid = new byte[64];
        Array.Fill(largestValid, (byte)0xFF); // 2^512 - 1, the largest 64-byte value

        var tooLarge = new byte[65];
        tooLarge[0] = 0x01;
        tooLarge[1] = 0xFF; // 2^512 + 0xFF * 2^504 — needs 65 bytes, still encodes to 88 chars

        string validEncoded = Base58.Bitcoin.Encode(largestValid);
        string tooLargeEncoded = Base58.Bitcoin.Encode(tooLarge);
        Assert.Equal(88, validEncoded.Length);
        Assert.Equal(88, tooLargeEncoded.Length); // same length, different byte count

        Assert.Equal(largestValid, Base58.DecodeBitcoin64Fast(validEncoded));
        Assert.Null(Base58.DecodeBitcoin64Fast(tooLargeEncoded));

        // The public API must fall back to the generic decoder and return all 65 bytes.
        Assert.Equal(tooLarge, Base58.Bitcoin.Decode(tooLargeEncoded));
    }

    [Fact]
    public void Decode32Fast_RejectsValueTooLargeFor32Bytes()
    {
        var largestValid = new byte[32];
        Array.Fill(largestValid, (byte)0xFF); // 2^256 - 1

        var tooLarge = new byte[33];
        tooLarge[0] = 0x01;
        tooLarge[1] = 0xFF; // 2^256 + 0xFF * 2^248

        string validEncoded = Base58.Bitcoin.Encode(largestValid);
        string tooLargeEncoded = Base58.Bitcoin.Encode(tooLarge);
        Assert.Equal(44, validEncoded.Length);
        Assert.Equal(44, tooLargeEncoded.Length);

        Assert.Equal(largestValid, Base58.DecodeBitcoin32Fast(validEncoded));
        Assert.Null(Base58.DecodeBitcoin32Fast(tooLargeEncoded));

        Assert.Equal(tooLarge, Base58.Bitcoin.Decode(tooLargeEncoded));
    }

    // The extreme case: every digit at its maximum. Asserts the decoded VALUE, not just the length, so
    // a fallback that returned the right size but wrong bytes would still fail.
    [Theory]
    [InlineData(88, 65)] // 58^88 - 1 needs 65 bytes
    [InlineData(44, 33)] // 58^44 - 1 needs 33 bytes
    public void Decode_AllMaxDigitsAtFastPathLength_FallsBackToGeneric(int chars, int expectedBytes)
    {
        string maxAtLength = new('z', chars); // 'z' is digit 57, so this is 58^chars - 1

        byte[] decoded = Base58.Bitcoin.Decode(maxAtLength);

        Assert.Equal(expectedBytes, decoded.Length);
        Assert.Equal(
            System.Numerics.BigInteger.Pow(58, chars) - 1,
            new System.Numerics.BigInteger(decoded, isUnsigned: true, isBigEndian: true));
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
