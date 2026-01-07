# Base58 Encoding Library

A .NET 9.0 Base58 encoding and decoding library with support for multiple alphabet variants.

## Features

- **Multiple Alphabets**: Built-in support for Bitcoin(IFPS/Sui), Ripple, and Flickr alphabets
- **Memory Efficient**: Uses stackalloc operations when possible to minimize allocations
- **Type Safe**: Leverages ReadOnlySpan and ReadOnlyMemory for safe memory operations
- **Intrinsics**: Uses SIMD `Vector128/Vector256` and unrolled loop for counting leading zeros

## Usage

```csharp
using Base58Encoding;

// Encode bytes to Base58 Bitcoin(IFPS/Sui) alphabet
byte[] data = { 0x01, 0x02, 0x03, 0x04 };
string encoded = Base58.Bitcoin.Encode(data);

// Decode Base58 string back to bytes
byte[] decoded = Base58.Bitcoin.Decode(encoded);

// Ripple / Flickr
Base58.Ripple.Encode(data);
Base58.Flickr.Encode(data);

// Custom
new Base58(Base58Alphabet.Custom(""));
```

## Benchmarks

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7462/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13700KF 3.40GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.1 (10.0.1, 10.0.125.57005), X64 RyuJIT x86-64-v3

Job=DefaultJob  

```
| Method                     | VectorType     | Mean        | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |--------------- |------------:|------:|-------:|----------:|------------:|
| **&#39;Our Base58 Encode&#39;**        | **BitcoinAddress** |   **537.07 ns** |  **1.00** | **0.0057** |      **96 B** |        **1.00** |
| &#39;SimpleBase Base58 Encode&#39; | BitcoinAddress |   782.31 ns |  1.46 | 0.0057 |      96 B |        1.00 |
| &#39;Our Base58 Decode&#39;        | BitcoinAddress |   168.95 ns |  0.31 | 0.0033 |      56 B |        0.58 |
| &#39;SimpleBase Base58 Decode&#39; | BitcoinAddress |   352.63 ns |  0.66 | 0.0033 |      56 B |        0.58 |
|                            |                |             |       |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **SolanaAddress**  |    **93.41 ns** |  **1.00** | **0.0070** |     **112 B** |        **1.00** |
| &#39;SimpleBase Base58 Encode&#39; | SolanaAddress  | 1,430.37 ns | 15.31 | 0.0057 |     112 B |        1.00 |
| &#39;Our Base58 Decode&#39;        | SolanaAddress  |   181.71 ns |  1.95 | 0.0035 |      56 B |        0.50 |
| &#39;SimpleBase Base58 Decode&#39; | SolanaAddress  |   837.03 ns |  8.96 | 0.0019 |      56 B |        0.50 |
|                            |                |             |       |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **SolanaTx**       |   **252.31 ns** |  **1.00** | **0.0124** |     **200 B** |        **1.00** |
| &#39;SimpleBase Base58 Encode&#39; | SolanaTx       | 7,247.09 ns | 28.73 | 0.0076 |     200 B |        1.00 |
| &#39;Our Base58 Decode&#39;        | SolanaTx       |   178.05 ns |  0.71 | 0.0055 |      88 B |        0.44 |
| &#39;SimpleBase Base58 Decode&#39; | SolanaTx       | 2,379.54 ns |  9.43 | 0.0038 |      88 B |        0.44 |
|                            |                |             |       |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **IPFSHash**       | **1,096.58 ns** |  **1.00** | **0.0076** |     **120 B** |        **1.00** |
| &#39;SimpleBase Base58 Encode&#39; | IPFSHash       | 1,644.83 ns |  1.50 | 0.0076 |     120 B |        1.00 |
| &#39;Our Base58 Decode&#39;        | IPFSHash       |   287.87 ns |  0.26 | 0.0038 |      64 B |        0.53 |
| &#39;SimpleBase Base58 Decode&#39; | IPFSHash       |   643.63 ns |  0.59 | 0.0038 |      64 B |        0.53 |
|                            |                |             |       |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **MoneroAddress**  | **4,998.35 ns** |  **1.00** | **0.0076** |     **216 B** |        **1.00** |
| &#39;SimpleBase Base58 Encode&#39; | MoneroAddress  | 8,585.92 ns |  1.72 |      - |     216 B |        1.00 |
| &#39;Our Base58 Decode&#39;        | MoneroAddress  | 1,173.48 ns |  0.23 | 0.0057 |      96 B |        0.44 |
| &#39;SimpleBase Base58 Decode&#39; | MoneroAddress  | 3,716.38 ns |  0.74 | 0.0038 |      96 B |        0.44 |


## License

This project is available under the MIT License.