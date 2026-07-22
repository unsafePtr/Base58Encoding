# Base58 Encoding Library

A .NET 10.0 Base58 encoding and decoding library with support for multiple alphabet variants.

## Features

- **Multiple Alphabets**: Built-in support for Bitcoin(IFPS/Sui/Solana), Ripple, and Flickr alphabets
- **Memory Efficient**: Uses stackalloc operations when possible to minimize allocations
- **Type Safe**: Leverages ReadOnlySpan and ReadOnlyMemory for safe memory operations
- **Intrinsics**: Uses SIMD `Vector256` and unrolled loop for counting leading zeros
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
- The 32/64-byte encode and decode matrix kernels are SIMD-accelerated with `Vector256`/`Vector128` (widest available width, scalar fallback) — in addition to the vectorized leading-zero count
- Separate encode/decode tables for 32-byte and 64-byte fixed sizes
- Achieves ~2.5x speedup through table-based optimizations vs iterative division

**References:**
- [Firedancer C implementation](https://github.com/firedancer-io/firedancer/tree/main/src/ballet/base58)


## Benchmarks

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8655/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13700KF 3.40GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 11.0.100-preview.3.26207.106
  [Host]     : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=DefaultJob  

```
| Method                     | Categories | VectorType     | Mean        | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |----------- |--------------- |------------:|------:|-------:|----------:|------------:|
| **&#39;Our Base58 Decode&#39;**        | **Decode**     | **BitcoinAddress** |   **178.71 ns** |  **1.00** | **0.0033** |      **56 B** |        **1.00** |
| &#39;SimpleBase Base58 Decode&#39; | Decode     | BitcoinAddress |   364.51 ns |  2.04 | 0.0033 |      56 B |        1.00 |
|                            |            |                |             |       |        |           |             |
| **&#39;Our Base58 Decode&#39;**        | **Decode**     | **SolanaAddress**  |    **83.27 ns** |  **1.00** | **0.0035** |      **56 B** |        **1.00** |
| &#39;SimpleBase Base58 Decode&#39; | Decode     | SolanaAddress  |   598.64 ns |  7.19 | 0.0029 |      56 B |        1.00 |
|                            |            |                |             |       |        |           |             |
| **&#39;Our Base58 Decode&#39;**        | **Decode**     | **SolanaTx**       |   **167.00 ns** |  **1.00** | **0.0055** |      **88 B** |        **1.00** |
| &#39;SimpleBase Base58 Decode&#39; | Decode     | SolanaTx       | 4,190.80 ns | 25.10 | 0.0038 |      88 B |        1.00 |
|                            |            |                |             |       |        |           |             |
| **&#39;Our Base58 Decode&#39;**        | **Decode**     | **IPFSHash**       |   **339.26 ns** |  **1.00** | **0.0038** |      **64 B** |        **1.00** |
| &#39;SimpleBase Base58 Decode&#39; | Decode     | IPFSHash       |   641.17 ns |  1.89 | 0.0038 |      64 B |        1.00 |
|                            |            |                |             |       |        |           |             |
| **&#39;Our Base58 Decode&#39;**        | **Decode**     | **MoneroAddress**  | **1,391.11 ns** |  **1.00** | **0.0057** |      **96 B** |        **1.00** |
| &#39;SimpleBase Base58 Decode&#39; | Decode     | MoneroAddress  | 3,882.84 ns |  2.79 |      - |      96 B |        1.00 |
|                            |            |                |             |       |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **Encode**     | **BitcoinAddress** |   **532.69 ns** |  **1.00** | **0.0057** |      **96 B** |        **1.00** |
| &#39;SimpleBase Base58 Encode&#39; | Encode     | BitcoinAddress |   774.83 ns |  1.45 | 0.0057 |      96 B |        1.00 |
|                            |            |                |             |       |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **Encode**     | **SolanaAddress**  |    **95.15 ns** |  **1.00** | **0.0070** |     **112 B** |        **1.00** |
| &#39;SimpleBase Base58 Encode&#39; | Encode     | SolanaAddress  | 1,521.61 ns | 15.99 | 0.0057 |     112 B |        1.00 |
|                            |            |                |             |       |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **Encode**     | **SolanaTx**       |   **196.50 ns** |  **1.00** | **0.0126** |     **200 B** |        **1.00** |
| &#39;SimpleBase Base58 Encode&#39; | Encode     | SolanaTx       | 7,338.14 ns | 37.34 | 0.0076 |     200 B |        1.00 |
|                            |            |                |             |       |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **Encode**     | **IPFSHash**       | **1,085.19 ns** |  **1.00** | **0.0076** |     **120 B** |        **1.00** |
| &#39;SimpleBase Base58 Encode&#39; | Encode     | IPFSHash       | 1,690.70 ns |  1.56 | 0.0076 |     120 B |        1.00 |
|                            |            |                |             |       |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **Encode**     | **MoneroAddress**  | **4,959.86 ns** |  **1.00** | **0.0076** |     **216 B** |        **1.00** |
| &#39;SimpleBase Base58 Encode&#39; | Encode     | MoneroAddress  | 8,734.32 ns |  1.76 |      - |     216 B |        1.00 |
