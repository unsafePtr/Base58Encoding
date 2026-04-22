# Base58Encoding

A high-performance .NET 10 Base58 encoding and decoding library with support for multiple alphabet variants.

## Features

- **Multiple Alphabets**: Built-in support for Bitcoin (IPFS/Sui/Solana), Ripple, and Flickr alphabets
- **Optimized Hot Paths**: Firedancer-based fast paths for 32-byte and 64-byte inputs (up to 15x faster than SimpleBase)
- **Zero-allocation API**: Encode and decode directly into caller-owned buffers
- **SIMD**: Uses `Vector256` for counting leading zeros

## Usage

### Allocating API

```csharp
using Base58Encoding;

byte[] data = { 0x01, 0x02, 0x03, 0x04 };

string encoded = Base58.Bitcoin.Encode(data);
byte[] decoded = Base58.Bitcoin.Decode(encoded);

// Ripple / Flickr alphabets
Base58.Ripple.Encode(data);
Base58.Flickr.Encode(data);
```

### Zero-allocation API

```csharp
using Base58Encoding;

byte[] data = { 0x01, 0x02, 0x03, 0x04 };

// Encode into a caller-owned buffer
Span<byte> encodedBytes = stackalloc byte[Base58.GetMaxEncodedLength(data.Length)];
int written = Base58.Bitcoin.Encode(data, encodedBytes);

// Decode from char span or ASCII byte span into a caller-owned buffer
Span<byte> decodedBytes = stackalloc byte[Base58.GetTypicalDecodedLength(written)];
int decodedLen = Base58.Bitcoin.Decode(encodedBytes[..written], decodedBytes);
```

## License

MIT
