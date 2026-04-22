# Base58 Encoding Library

A .NET 10.0 Base58 encoding and decoding library with support for multiple alphabet variants.

## Features

- **Multiple Alphabets**: Built-in support for Bitcoin(IFPS/Sui/Solana), Ripple, and Flickr alphabets
- **Memory Efficient**: Uses stackalloc operations when possible to minimize allocations
- **Type Safe**: Leverages ReadOnlySpan and ReadOnlyMemory for safe memory operations
- **Intrinsics**: Uses SIMD `Vector128/Vector256` and unrolled loop for counting leading zeros
- **Optimized Hot Paths**: Fast fixed-length encode/decode for 32-byte and 64-byte inputs using Firedancer-like optimizations

## Usage

### Allocating API

```csharp
using Base58Encoding;

// Encode bytes to Base58 string (Bitcoin / IPFS / Sui / Solana alphabet)
byte[] data = { 0x01, 0x02, 0x03, 0x04 };
string encoded = Base58.Bitcoin.Encode(data);

// Decode Base58 string back to bytes
byte[] decoded = Base58.Bitcoin.Decode(encoded);

// Ripple / Flickr alphabets
Base58.Ripple.Encode(data);
Base58.Flickr.Encode(data);
```

### Zero-allocation API

Encode or decode directly into a caller-owned buffer — no heap allocations on the hot path.

```csharp
using Base58Encoding;

byte[] data = { 0x01, 0x02, 0x03, 0x04 };

// Size the output buffer using the helper
int maxLen = Base58.GetMaxEncodedLength(data.Length);
Span<byte> encodedBytes = stackalloc byte[maxLen]; // or rent from ArrayPool

int written = Base58.Bitcoin.Encode(data, encodedBytes);
ReadOnlySpan<byte> result = encodedBytes[..written]; // ASCII bytes

// Decode from a char span or ASCII byte span into a caller-owned buffer
Span<byte> decodedBytes = stackalloc byte[Base58.GetTypicalDecodedLength(written)];
int decodedLen = Base58.Bitcoin.Decode(result, decodedBytes);

// Both Decode overloads are supported:
//   int Decode(ReadOnlySpan<char>  encoded, Span<byte> destination)
//   int Decode(ReadOnlySpan<byte>  encoded, Span<byte> destination)
```

`GetMaxEncodedLength(byteCount)` returns a safe upper bound for the encoded output size.  
`GetTypicalDecodedLength(encodedLength)` returns a typical upper bound for decoded output (see its XML doc for the edge case around leading `'1'` characters).

## Performance

The library automatically uses optimized fast paths for common fixed-size inputs:
- **32-byte inputs** (Bitcoin/Solana addresses, SHA-256 hashes): 8.5x faster encoding
- **64-byte inputs** (SHA-512 hashes): Similar performance improvements

These optimizations are based on Firedancer's specialized Base58 algorithms and are transparent to the user. Unlike Firedancer however, we fallback to the generic approach in case of edge-cases.

**Algorithm Details:**
- Uses **Mixed Radix Conversion (MRC)** with intermediate base 58^5 representation
- Precomputed multiplication tables replace expensive division operations
- Converts binary data to base 58^5 limbs, then to raw base58 digits
- Matrix multiplication approach processes 5 base58 digits simultaneously
- Separate encode/decode tables for 32-byte and 64-byte fixed sizes
- Achieves ~2.5x speedup through table-based optimizations vs iterative division

**References:**
- [Firedancer C implementation](https://github.com/firedancer-io/firedancer/tree/main/src/ballet/base58)


## Benchmarks

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13700KF 3.40GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v3

Job=DefaultJob  

```
| Method                     | VectorType     | Mean        | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |--------------- |------------:|------:|-------:|----------:|------------:|
| **&#39;Our Base58 Encode&#39;**        | **BitcoinAddress** |   **537.17 ns** |  **1.00** | **0.0057** |      **96 B** |        **1.00** |
| &#39;SimpleBase Base58 Encode&#39; | BitcoinAddress |   776.69 ns |  1.45 | 0.0057 |      96 B |        1.00 |
| &#39;Our Base58 Decode&#39;        | BitcoinAddress |   160.88 ns |  0.30 | 0.0033 |      56 B |        0.58 |
| &#39;SimpleBase Base58 Decode&#39; | BitcoinAddress |   353.19 ns |  0.66 | 0.0033 |      56 B |        0.58 |
|                            |                |             |       |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **SolanaAddress**  |    **94.07 ns** |  **1.00** | **0.0070** |     **112 B** |        **1.00** |
| &#39;SimpleBase Base58 Encode&#39; | SolanaAddress  | 1,433.92 ns | 15.24 | 0.0057 |     112 B |        1.00 |
| &#39;Our Base58 Decode&#39;        | SolanaAddress  |   104.19 ns |  1.11 | 0.0035 |      56 B |        0.50 |
| &#39;SimpleBase Base58 Decode&#39; | SolanaAddress  |   703.66 ns |  7.48 | 0.0029 |      56 B |        0.50 |
|                            |                |             |       |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **SolanaTx**       |   **239.21 ns** |  **1.00** | **0.0124** |     **200 B** |        **1.00** |
| &#39;SimpleBase Base58 Encode&#39; | SolanaTx       | 7,166.10 ns | 29.96 | 0.0076 |     200 B |        1.00 |
| &#39;Our Base58 Decode&#39;        | SolanaTx       |   180.37 ns |  0.75 | 0.0055 |      88 B |        0.44 |
| &#39;SimpleBase Base58 Decode&#39; | SolanaTx       | 2,957.77 ns | 12.36 | 0.0038 |      88 B |        0.44 |
|                            |                |             |       |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **IPFSHash**       | **1,084.69 ns** |  **1.00** | **0.0076** |     **120 B** |        **1.00** |
| &#39;SimpleBase Base58 Encode&#39; | IPFSHash       | 1,617.11 ns |  1.49 | 0.0076 |     120 B |        1.00 |
| &#39;Our Base58 Decode&#39;        | IPFSHash       |   318.15 ns |  0.29 | 0.0038 |      64 B |        0.53 |
| &#39;SimpleBase Base58 Decode&#39; | IPFSHash       |   854.47 ns |  0.79 | 0.0038 |      64 B |        0.53 |
|                            |                |             |       |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **MoneroAddress**  | **4,917.65 ns** |  **1.00** | **0.0076** |     **216 B** |        **1.00** |
| &#39;SimpleBase Base58 Encode&#39; | MoneroAddress  | 8,621.98 ns |  1.75 |      - |     216 B |        1.00 |
| &#39;Our Base58 Decode&#39;        | MoneroAddress  | 1,198.92 ns |  0.24 | 0.0057 |      96 B |        0.44 |
| &#39;SimpleBase Base58 Decode&#39; | MoneroAddress  | 3,844.43 ns |  0.78 |      - |      96 B |        0.44 |


## License

This project is available under the MIT License.
