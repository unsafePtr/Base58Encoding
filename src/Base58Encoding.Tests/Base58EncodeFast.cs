namespace Base58Encoding.Tests;

public class Base58EncodeFast
{

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
}
