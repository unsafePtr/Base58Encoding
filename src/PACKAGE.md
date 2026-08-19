# Base58Encoding

A high-performance .NET 10 Base58 encoding and decoding library with support for multiple alphabet variants.

## Alphabets

| Property | First Character | Used By |
|---|---|---|
| `Base58.Bitcoin` | `1` | Bitcoin, IPFS, Solana, Sui, Monero |
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

## Native AOT

Reflection-free and marked `IsAotCompatible`, so `PublishAot` and `PublishTrimmed` need no configuration.

Note that `Vector256` is not used under Native AOT: ILC fixes `Vector256.IsHardwareAccelerated` at build time against a baseline without AVX2, so AOT builds run the `Vector128` kernel.

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
