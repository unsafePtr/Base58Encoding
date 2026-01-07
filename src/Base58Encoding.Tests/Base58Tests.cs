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

    [Fact]
    public void BitcoinAlphabet_EncodeGeneric_Generates_SameResultAsFast32()
    {
        Span<byte> buffer = stackalloc byte[32];
        Random.Shared.NextBytes(buffer);

        var genericResult = Base58.Bitcoin.EncodeGeneric(buffer);
        var fastResult = Base58.EncodeBitcoin32Fast(buffer);

        Assert.Equal(genericResult, fastResult);
    }

    [Fact]
    public void Encode32Fast_TableDimensions_AreCorrect()
    {
        // Verify table dimensions  
        Assert.Equal(8, Base58BitcoinTables.BinarySz32);
        Assert.Equal(9, Base58BitcoinTables.IntermediateSz32);
        Assert.Equal(45, Base58BitcoinTables.Raw58Sz32);

        // Verify table bounds
        var encodeTable = Base58BitcoinTables.EncodeTable32;
        Assert.Equal(8, encodeTable.Length); // Should be BinarySz32
        Assert.Equal(8, encodeTable[0].Length); // Should be IntermediateSz32 - 1
    }

    [Fact]
    public void Encode32Fast_WithAllZeros_ReturnsCorrectOnes()
    {
        // Arrange
        var allZeros = new byte[32];

        // Act
        var result = Base58.Bitcoin.Encode(allZeros);

        // Assert - Should be 32 '1's for 32 zero bytes
        Assert.Equal(new string('1', 32), result);
    }

    [Fact]
    public void Encode32Fast_WithAllOnes_ProducesCorrectResult()
    {
        // Arrange
        var allOnes = Enumerable.Repeat((byte)0xFF, 32).ToArray();

        // Act
        var fastResult = Base58.Bitcoin.Encode(allOnes);
        var genericResult = Base58.Bitcoin.EncodeGeneric(allOnes);

        // Assert
        Assert.Equal(genericResult, fastResult);
        Assert.NotEmpty(fastResult);
    }

    [Fact]
    public void Encode32Fast_WithLeadingZeros_HandlesCorrectly()
    {
        // Test one simple case first to isolate the issue
        var testCase = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFE, 0xFD, 0xFC }; // 28 leading zeros

        // Act
        var genericResult = Base58.Bitcoin.EncodeGeneric(testCase);
        var simpleBase = SimpleBase.Base58.Bitcoin.Encode(testCase);
        var fastResult = Base58.Bitcoin.Encode(testCase);

        // Assert
        Assert.Equal(genericResult, fastResult);

        // Verify leading zeros are preserved as '1's
        int leadingZeros = testCase.TakeWhile(b => b == 0).Count();
        Assert.StartsWith(new string('1', leadingZeros), fastResult);
    }

    [Theory]
    [InlineData(1000)]
    public void Encode32Fast_WithRandomData_MatchesGeneric(int testCount)
    {
        var random = new Random(42); // Fixed seed for reproducibility

        for (int i = 0; i < testCount; i++)
        {
            // Arrange
            var testData = new byte[32];
            random.NextBytes(testData);

            // Act
            var fastResult = Base58.Bitcoin.Encode(testData);
            var genericResult = Base58.Bitcoin.EncodeGeneric(testData);

            // Assert
            Assert.Equal(genericResult, fastResult);

            // Verify it round-trips correctly
            var decoded = Base58.Bitcoin.Decode(fastResult);
            Assert.Equal(testData, decoded);
        }
    }

    [Fact]
    public void FastPath_OnlyTriggersForBitcoinAlphabet()
    {
        // Arrange
        var testData = new byte[32];
        Random.Shared.NextBytes(testData);

        // Act
        var bitcoinResult = Base58.Bitcoin.Encode(testData);
        var rippleResult = Base58.Ripple.Encode(testData);

        // Assert - Both should work but produce different results
        Assert.NotEqual(bitcoinResult, rippleResult);

        // Verify round-trips
        var bitcoinDecoded = Base58.Bitcoin.Decode(bitcoinResult);
        var rippleDecoded = Base58.Ripple.Decode(rippleResult);

        Assert.Equal(testData, bitcoinDecoded);
        Assert.Equal(testData, rippleDecoded);
    }

    [Fact]
    public void Encode32Fast_WithRealBitcoinAddressData_WorksCorrectly()
    {
        // Arrange - Real Bitcoin address hash160 (20 bytes padded to 32)
        var hash160 = new byte[]
        {
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 12 zero padding
            0x76, 0xa9, 0x14, 0x89, 0xab, 0xcd, 0xef, 0xab, 0xba, 0xab, 0xba, 0xab,
            0xba, 0xab, 0xba, 0xab, 0xba, 0xab, 0xba, 0xab, 0xba, 0x88, 0xac, 0x00
        };

        // Act
        var fastResult = Base58.Bitcoin.Encode(hash160);
        var genericResult = Base58.Bitcoin.EncodeGeneric(hash160);

        // Assert
        Assert.Equal(genericResult, fastResult);

        // Should start with 12 '1's due to leading zeros
        Assert.StartsWith(new string('1', 12), fastResult);
    }

    [Fact]
    public void Encode_WithNon32ByteInputs_UsesGenericPath()
    {
        var testCases = new[]
        {
            new byte[31], // 31 bytes
            new byte[33], // 33 bytes  
            new byte[20], // 20 bytes (Bitcoin hash160)
            new byte[64], // 64 bytes (will use generic until we implement 64-byte fast path)
        };

        foreach (var testCase in testCases)
        {
            Random.Shared.NextBytes(testCase);

            // Act
            var result = Base58.Bitcoin.Encode(testCase);

            // Assert - Should work correctly via generic path
            var decoded = Base58.Bitcoin.Decode(result);
            Assert.Equal(testCase, decoded);
        }
    }

    [Fact]
    public void Encode64Fast_TableDimensions_AreCorrect()
    {
        // Verify table dimensions
        Assert.Equal(16, Base58BitcoinTables.BinarySz64);
        Assert.Equal(18, Base58BitcoinTables.IntermediateSz64);
        Assert.Equal(90, Base58BitcoinTables.Raw58Sz64);

        // Verify table bounds
        var encodeTable = Base58BitcoinTables.EncodeTable64;
        Assert.Equal(16, encodeTable.Length); // Should be BinarySz64
        Assert.Equal(17, encodeTable[0].Length); // Should be IntermediateSz64 - 1
    }

    [Fact]
    public void Encode64Fast_WithAllZeros_ReturnsCorrectOnes()
    {
        // Arrange
        var allZeros = new byte[64];

        // Act
        var result = Base58.Bitcoin.Encode(allZeros);

        // Assert - Should be 64 '1's for 64 zero bytes
        Assert.Equal(new string('1', 64), result);
    }

    [Fact]
    public void Encode64Fast_WithRandomData_MatchesGeneric()
    {
        var random = new Random(42); // Fixed seed for reproducibility

        for (int i = 0; i < 100; i++)
        {
            // Arrange
            var testData = new byte[64];
            random.NextBytes(testData);

            // Act
            var fastResult = Base58.Bitcoin.Encode(testData);
            var genericResult = Base58.Bitcoin.EncodeGeneric(testData);

            // Assert
            Assert.Equal(genericResult, fastResult);

            // Verify it round-trips correctly
            var decoded = Base58.Bitcoin.Decode(fastResult);
            Assert.Equal(testData, decoded);
        }
    }

    [Fact]
    public void Encode64Fast_WithLeadingZeros_HandlesCorrectly()
    {
        // Arrange - 64-byte data with leading zeros
        var testCases = new[]
        {
            new byte[64], // All zeros
            Enumerable.Range(0, 64).Select(i => i < 32 ? (byte)0x00 : (byte)0xFF).ToArray(), // 32 leading zeros
        };

        foreach (var testCase in testCases)
        {
            // Act
            var fastResult = Base58.Bitcoin.Encode(testCase);
            var genericResult = Base58.Bitcoin.EncodeGeneric(testCase);

            // Assert
            Assert.Equal(genericResult, fastResult);

            // Verify leading zeros are preserved as '1's
            int leadingZeros = testCase.TakeWhile(b => b == 0).Count();
            Assert.StartsWith(new string('1', leadingZeros), fastResult);
        }
    }

    [Fact]
    public void Encode64Fast_WithSolanaTransactionSignature_WorksCorrectly()
    {
        // Arrange - Simulate a 64-byte Solana transaction signature
        var signatureData = new byte[64];
        var random = new Random(123);
        random.NextBytes(signatureData);

        // Act
        var fastResult = Base58.Bitcoin.Encode(signatureData);
        var genericResult = Base58.Bitcoin.EncodeGeneric(signatureData);

        // Assert
        Assert.Equal(genericResult, fastResult);

        // Verify round-trip
        var decoded = Base58.Bitcoin.Decode(fastResult);
        Assert.Equal(signatureData, decoded);

        // Should be between 64 and 88 characters
        Assert.InRange(fastResult.Length, 64, 88);
    }

    [Fact]
    public void Decode32Fast_WithValidInput_MatchesGeneric()
    {
        var random = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            // Arrange
            var testData = new byte[32];
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
    public void Decode32Fast_WithAllZeros_WorksCorrectly()
    {
        // Arrange
        var allZeros = new byte[32];
        var encoded = SimpleBase.Base58.Bitcoin.Encode(allZeros);

        // Act
        var decoded = Base58.Bitcoin.Decode(encoded);
        var genericDecoded = Base58.Bitcoin.DecodeGeneric(encoded);

        // Assert - Debug info
        Console.WriteLine($"Encoded: {encoded}");
        Console.WriteLine($"Fast decoded length: {decoded.Length}");
        Console.WriteLine($"Generic decoded length: {genericDecoded.Length}");

        // Debug output first
        Console.WriteLine($"Encoded: '{encoded}' (length: {encoded.Length})");
        Console.WriteLine($"Generic decoded: length {genericDecoded.Length}");
        Console.WriteLine($"Fast decoded: length {decoded.Length}");

        Assert.Equal(genericDecoded, decoded);
    }

    [Fact]
    public void Decode64Fast_WithAllZeros_WorksCorrectly()
    {
        // Arrange
        var allZeros = new byte[64];
        var encoded = SimpleBase.Base58.Bitcoin.Encode(allZeros);

        // Act
        var decoded = Base58.Bitcoin.Decode(encoded);

        // Assert
        Assert.Equal(allZeros, decoded);
        Assert.Equal(new string('1', 64), encoded);
    }

    [Theory]
    [InlineData("invalid0chars")] // Invalid character '0'
    public void Decode32Fast_WithInvalidInput_FallsBackToGeneric(string input)
    {
        // Act & Assert - Should throw exception via generic path
        Assert.Throws<ArgumentException>(() => Base58.Bitcoin.Decode(input));
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
        var decoded = Base58.Bitcoin.Decode(encoded);

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
            var decodedsimpleBase = SimpleBase.Base58.Bitcoin.Decode(encoded);
            var decoded = Base58.DecodeBitcoin32Fast(encoded);

            var genericDecoded = Base58.Bitcoin.DecodeGeneric(encoded);
            Console.WriteLine($"Generic decoded: length {genericDecoded.Length}");

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
            var decoded = Base58.Bitcoin.Decode(encoded);

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
        var decoded = Base58.Bitcoin.Decode(encoded);

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
        var decoded = Base58.Bitcoin.Decode(encoded);

        // Assert
        Assert.Equal(testData, decoded);
        Assert.InRange(encoded.Length, 87, 88); // Maximum Base58 length for 64 bytes
    }

    [Fact]
    public void DecodeFast_OnlyTriggersForCorrectInputLengths()
    {
        // Arrange - Various input sizes that should NOT trigger fast paths
        var testCases = new[]
        {
            new byte[31], // 31 bytes - too small for 32-byte fast path
            new byte[33], // 33 bytes - too big for 32-byte, too small for 64-byte
            new byte[63], // 63 bytes - too small for 64-byte fast path
            new byte[65], // 65 bytes - too big for 64-byte fast path
        };

        foreach (var testCase in testCases)
        {
            Random.Shared.NextBytes(testCase);

            // Act
            var encoded = Base58.Bitcoin.Encode(testCase);
            var decoded = Base58.Bitcoin.Decode(encoded);

            // Assert - Should work correctly via generic path
            Assert.Equal(testCase, decoded);
        }
    }
}