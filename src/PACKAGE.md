# Base58Encoding

A high-performance .NET 10 Base58 encoding and decoding library with support for multiple alphabet variants.

## Features

- **Multiple Alphabets**: Built-in support for Bitcoin (IPFS/Sui/Solana), Ripple, and Flickr alphabets
- **Optimized Hot Paths**: Firedancer-based fast paths for 32-byte and 64-byte inputs (up to 15x faster than SimpleBase)
- **Zero-allocation API**: Encode and decode directly into caller-owned buffers
- **SIMD**: Uses `Vector256` for counting leading zeros

## Alphabets

| Property | First Character | Used By |
|---|---|---|
| `Base58.Bitcoin` | `1` | Bitcoin, IPFS, Solana, Sui |
| `Base58.Ripple` | `r` | Ripple (XRP) |
| `Base58.Flickr` | `1` | Flickr short URLs |

## API

### Allocating API

```csharp
string Encode(ReadOnlySpan<byte> data)
```
Encodes `data` and returns a new Base58 string. Returns `""` for empty input.

```csharp
byte[] Decode(ReadOnlySpan<char> encoded)
```
Decodes a Base58 string and returns a new byte array. Throws `ArgumentException` on invalid characters.

### Zero-allocation API

```csharp
int Encode(ReadOnlySpan<byte> data, Span<byte> destination)
```
Encodes `data` as ASCII Base58 bytes into `destination`. Returns the number of bytes written.
Throws `ArgumentException` if `destination` is too small. Use `Base58.GetMaxEncodedLength` to size the buffer.

```csharp
int Decode(ReadOnlySpan<char> encoded, Span<byte> destination)
int Decode(ReadOnlySpan<byte> encoded, Span<byte> destination)
```
Decodes Base58 chars (or ASCII bytes) into `destination`. Returns the number of bytes written.
Throws `ArgumentException` on invalid characters or if `destination` is too small.
Use `Base58.GetTypicalDecodedLength` to size the buffer for typical inputs.

### Buffer sizing helpers

```csharp
static int Base58.GetMaxEncodedLength(int byteCount)
```
Returns a safe upper bound for the number of Base58 characters produced from `byteCount` bytes.
Formula: `byteCount * 138 / 100 + 1`. Use this to size the `destination` buffer for `Encode`.

```csharp
static int Base58.GetTypicalDecodedLength(int encodedLength)
```
Returns a typical upper bound for the decoded byte count from an encoded input of `encodedLength` characters.
Formula: `encodedLength * 733 / 1000 + 1`. Suitable for inputs without leading `1` characters.
For inputs that may contain leading `1`s, size the destination at `encodedLength` (safe upper bound).

## Usage

### Allocating API

```csharp
using Base58Encoding;

byte[] data = { 0x01, 0x02, 0x03, 0x04 };

string encoded = Base58.Bitcoin.Encode(data);
byte[] decoded = Base58.Bitcoin.Decode(encoded);

// Ripple / Flickr alphabets
string ripple  = Base58.Ripple.Encode(data);
string flickr  = Base58.Flickr.Encode(data);
```

### Zero-allocation API

```csharp
using Base58Encoding;

byte[] data = { 0x01, 0x02, 0x03, 0x04 };

// Encode into a caller-owned buffer
Span<byte> encodedBytes = stackalloc byte[Base58.GetMaxEncodedLength(data.Length)];
int written = Base58.Bitcoin.Encode(data, encodedBytes);

// Decode from a byte span into a caller-owned buffer
Span<byte> decodedBytes = stackalloc byte[Base58.GetTypicalDecodedLength(written)];
int decodedLen = Base58.Bitcoin.Decode(encodedBytes[..written], decodedBytes);
```

### Custom alphabet

Implement `IBase58Alphabet` with a `struct` to define your own character set:

```csharp
using Base58Encoding;

public struct MyAlphabet : IBase58Alphabet
{
    private static ReadOnlySpan<byte> Chars =>
        "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz"u8;

    public static ReadOnlySpan<byte> Characters => Chars;
    public static ReadOnlySpan<byte> DecodeTable { get; } = Base58Alphabet.BuildDecodeTable(Chars);
    public static byte FirstCharacter => (byte)'1';
}

var codec = new Base58<MyAlphabet>();
string encoded = codec.Encode(data);
```

## License

MIT
