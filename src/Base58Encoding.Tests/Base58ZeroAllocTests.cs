using System.Text;

namespace Base58Encoding.Tests;

public class Base58ZeroAllocTests
{
    [Fact]
    public void GetEncodedLength_ReturnsUpperBound()
    {
        Assert.Equal(0, Base58.GetEncodedLength(0));
        Assert.True(Base58.GetEncodedLength(1) >= 2);
        Assert.True(Base58.GetEncodedLength(32) >= 44);
        Assert.True(Base58.GetEncodedLength(64) >= 88);
    }

    [Fact]
    public void GetEncodedLength_NegativeThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Base58.GetEncodedLength(-1));
    }

    [Fact]
    public void GetTypicalDecodedLength_ReturnsTypicalBound()
    {
        Assert.Equal(0, Base58.GetTypicalDecodedLength(0));
        Assert.True(Base58.GetTypicalDecodedLength(44) >= 32);
        Assert.True(Base58.GetTypicalDecodedLength(88) >= 64);
    }

    [Fact]
    public void GetTypicalDecodedLength_NegativeThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Base58.GetTypicalDecodedLength(-1));
    }

    [Fact]
    public void Encode_ToBytes_MatchesStringEncode()
    {
        var random = new Random(42);
        int[] sizes = [0, 1, 5, 20, 32, 33, 64, 100];

        foreach (int size in sizes)
        {
            var data = new byte[size];
            random.NextBytes(data);

            string expected = Base58.Bitcoin.Encode(data);

            Span<byte> buffer = stackalloc byte[Base58.GetEncodedLength(data.Length)];
            int written = Base58.Bitcoin.Encode(data, buffer);

            string actual = Encoding.ASCII.GetString(buffer[..written]);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Encode_ToBytes_WithLeadingZeros_PreservesOnes()
    {
        byte[] data = [0x00, 0x00, 0x00, 0x01, 0x02];
        Span<byte> buffer = stackalloc byte[Base58.GetEncodedLength(data.Length)];
        int written = Base58.Bitcoin.Encode(data, buffer);

        string actual = Encoding.ASCII.GetString(buffer[..written]);
        Assert.Equal(Base58.Bitcoin.Encode(data), actual);
        Assert.StartsWith("111", actual);
    }

    [Fact]
    public void Encode_ToBytes_AllZeros_WritesAllOnes()
    {
        byte[] data = new byte[10];
        Span<byte> buffer = stackalloc byte[Base58.GetEncodedLength(data.Length)];
        int written = Base58.Bitcoin.Encode(data, buffer);

        Assert.Equal(10, written);
        for (int i = 0; i < written; i++)
        {
            Assert.Equal((byte)'1', buffer[i]);
        }
    }

    [Fact]
    public void Encode_ToBytes_Empty_Returns0()
    {
        Span<byte> buffer = stackalloc byte[4];
        int written = Base58.Bitcoin.Encode([], buffer);
        Assert.Equal(0, written);
    }

    [Fact]
    public void Encode_ToBytes_DestinationTooSmall_Throws()
    {
        byte[] data = [0xFF, 0xFF, 0xFF, 0xFF];
        byte[] tooSmall = new byte[1];
        Assert.Throws<ArgumentException>(() => Base58.Bitcoin.Encode(data, tooSmall));
    }

    [Fact]
    public void Encode_ToBytes_Bitcoin32Fast_Works()
    {
        byte[] data = new byte[32];
        new Random(1).NextBytes(data);

        string expected = Base58.Bitcoin.Encode(data);

        Span<byte> buffer = stackalloc byte[Base58.GetEncodedLength(32)];
        int written = Base58.Bitcoin.Encode(data, buffer);
        string actual = Encoding.ASCII.GetString(buffer[..written]);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Encode_ToBytes_Bitcoin64Fast_Works()
    {
        byte[] data = new byte[64];
        new Random(2).NextBytes(data);

        string expected = Base58.Bitcoin.Encode(data);

        Span<byte> buffer = stackalloc byte[Base58.GetEncodedLength(64)];
        int written = Base58.Bitcoin.Encode(data, buffer);
        string actual = Encoding.ASCII.GetString(buffer[..written]);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Decode_FromChars_MatchesByteArrayDecode()
    {
        var random = new Random(3);
        int[] sizes = [1, 5, 20, 32, 64];

        foreach (int size in sizes)
        {
            var data = new byte[size];
            random.NextBytes(data);
            string encoded = Base58.Bitcoin.Encode(data);

            byte[] expected = Base58.Bitcoin.Decode(encoded);
            Span<byte> buffer = stackalloc byte[64];
            int written = Base58.Bitcoin.Decode(encoded.AsSpan(), buffer);

            Assert.Equal(expected, buffer[..written].ToArray());
            Assert.Equal(data, buffer[..written].ToArray());
        }
    }

    [Fact]
    public void Decode_FromUtf8Bytes_MatchesFromChars()
    {
        var random = new Random(4);
        int[] sizes = [1, 5, 20, 32, 64];

        foreach (int size in sizes)
        {
            var data = new byte[size];
            random.NextBytes(data);
            string encoded = Base58.Bitcoin.Encode(data);
            byte[] utf8 = Encoding.ASCII.GetBytes(encoded);

            Span<byte> charBuf = stackalloc byte[64];
            int fromChars = Base58.Bitcoin.Decode(encoded.AsSpan(), charBuf);

            Span<byte> byteBuf = stackalloc byte[64];
            int fromBytes = Base58.Bitcoin.Decode((ReadOnlySpan<byte>)utf8, byteBuf);

            Assert.Equal(fromChars, fromBytes);
            Assert.Equal(charBuf[..fromChars].ToArray(), byteBuf[..fromBytes].ToArray());
            Assert.Equal(data, byteBuf[..fromBytes].ToArray());
        }
    }

    [Fact]
    public void Decode_FromChars_DestinationTooSmall_Throws()
    {
        string encoded = Base58.Bitcoin.Encode(new byte[32]);
        byte[] tooSmall = new byte[1];
        Assert.Throws<ArgumentException>(() => Base58.Bitcoin.Decode(encoded.AsSpan(), tooSmall));
    }

    [Fact]
    public void Decode_FromBytes_DestinationTooSmall_Throws()
    {
        byte[] encoded = Encoding.ASCII.GetBytes(Base58.Bitcoin.Encode(new byte[32]));
        byte[] tooSmall = new byte[1];
        Assert.Throws<ArgumentException>(() => Base58.Bitcoin.Decode((ReadOnlySpan<byte>)encoded, tooSmall));
    }

    [Theory]
    [InlineData("0abc")]
    [InlineData("Iabc")]
    [InlineData("Oabc")]
    [InlineData("labc")]
    public void Decode_FromChars_InvalidChar_Throws(string encoded)
    {
        byte[] buffer = new byte[64];
        Assert.Throws<ArgumentException>(() => Base58.Bitcoin.Decode(encoded.AsSpan(), buffer));
    }

    [Theory]
    [InlineData("0abc")]
    [InlineData("Iabc")]
    [InlineData("Oabc")]
    [InlineData("labc")]
    public void Decode_FromBytes_InvalidByte_Throws(string encoded)
    {
        byte[] utf8 = Encoding.ASCII.GetBytes(encoded);
        byte[] buffer = new byte[64];
        Assert.Throws<ArgumentException>(() => Base58.Bitcoin.Decode((ReadOnlySpan<byte>)utf8, buffer));
    }

    [Fact]
    public void Decode_AllOnes_ReturnsAllZeroBytes()
    {
        string encoded = new('1', 10);
        Span<byte> buffer = stackalloc byte[10];
        int written = Base58.Bitcoin.Decode(encoded.AsSpan(), buffer);

        Assert.Equal(10, written);
        for (int i = 0; i < written; i++)
        {
            Assert.Equal(0, buffer[i]);
        }
    }

    [Fact]
    public void Decode_Empty_Returns0()
    {
        Span<byte> buffer = stackalloc byte[4];
        int written = Base58.Bitcoin.Decode(ReadOnlySpan<char>.Empty, buffer);
        Assert.Equal(0, written);

        written = Base58.Bitcoin.Decode(ReadOnlySpan<byte>.Empty, buffer);
        Assert.Equal(0, written);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(16)]
    [InlineData(31)]
    public void Decode_Bitcoin32Fast_VariousLeadingZeros(int leadingZeros)
    {
        var data = new byte[32];
        new Random(leadingZeros).NextBytes(data.AsSpan(leadingZeros));
        string encoded = Base58.Bitcoin.Encode(data);

        Span<byte> buffer = stackalloc byte[32];
        int written = Base58.Bitcoin.Decode(encoded.AsSpan(), buffer);

        Assert.Equal(32, written);
        Assert.Equal(data, buffer.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(32)]
    [InlineData(63)]
    public void Decode_Bitcoin64Fast_VariousLeadingZeros(int leadingZeros)
    {
        var data = new byte[64];
        new Random(leadingZeros).NextBytes(data.AsSpan(leadingZeros));
        string encoded = Base58.Bitcoin.Encode(data);

        Span<byte> buffer = stackalloc byte[64];
        int written = Base58.Bitcoin.Decode(encoded.AsSpan(), buffer);

        Assert.Equal(64, written);
        Assert.Equal(data, buffer.ToArray());
    }

    [Fact]
    public void Encode_Decode_RoundTrip_Ripple()
    {
        var data = new byte[20];
        new Random(5).NextBytes(data);

        Span<byte> encBuf = stackalloc byte[Base58.GetEncodedLength(data.Length)];
        int encWritten = Base58.Ripple.Encode(data, encBuf);

        Span<byte> decBuf = stackalloc byte[data.Length];
        int decWritten = Base58.Ripple.Decode(encBuf[..encWritten], decBuf);

        Assert.Equal(data.Length, decWritten);
        Assert.Equal(data, decBuf[..decWritten].ToArray());
    }

    [Fact]
    public void Decode_FastPathFallback_LeavesSentinelBeyondWritten()
    {
        // Construct an encoded string that will trigger the 32-byte fast path
        // (encoded.Length in [32..44]) but actually represents fewer than 32 bytes.
        // This forces the leading-zero mismatch check to return -1, falling back
        // to the generic decoder which writes fewer bytes than the fast path did.

        // 24 bytes of non-zero data encodes to ~32-33 chars → triggers fast path.
        var data = new byte[24];
        data[0] = 0xFF;
        new Random(42).NextBytes(data.AsSpan(1));

        string encoded = Base58.Bitcoin.Encode(data);
        Assert.InRange(encoded.Length, 32, 44); // confirms fast-path range

        // Oversize destination, pre-filled with sentinel to detect fast-path stale writes.
        Span<byte> destination = stackalloc byte[64];
        destination.Fill(0xAA);

        int written = Base58.Bitcoin.Decode(encoded.AsSpan(), destination);

        // Returned data is correct.
        Assert.Equal(24, written);
        Assert.Equal(data, destination[..written].ToArray());

        // Bytes beyond `written` should still be the sentinel.
        // This passes with the current temp-buffer implementation (fast path never
        // touches destination on the -1 fallback path). If the temp buffer is
        // removed, bytes [24..32] will contain fast-path leftover = data[16..24].
        for (int i = written; i < destination.Length; i++)
        {
            Assert.Equal(0xAA, destination[i]);
        }
    }

    [Fact]
    public void Encode_Decode_RoundTrip_Flickr()
    {
        var data = new byte[20];
        new Random(6).NextBytes(data);

        Span<byte> encBuf = stackalloc byte[Base58.GetEncodedLength(data.Length)];
        int encWritten = Base58.Flickr.Encode(data, encBuf);

        Span<byte> decBuf = stackalloc byte[data.Length];
        int decWritten = Base58.Flickr.Decode(encBuf[..encWritten], decBuf);

        Assert.Equal(data.Length, decWritten);
        Assert.Equal(data, decBuf[..decWritten].ToArray());
    }
}
