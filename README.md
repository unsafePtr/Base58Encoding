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
BenchmarkDotNet v0.15.2, Windows 11 (10.0.26100.4946/24H2/2024Update/HudsonValley)
13th Gen Intel Core i7-13700KF 3.40GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 9.0.304
  [Host]   : .NET 9.0.8 (9.0.825.36511), X64 RyuJIT AVX2
  .NET 9.0 : .NET 9.0.8 (9.0.825.36511), X64 RyuJIT AVX2

Job=.NET 9.0  Runtime=.NET 9.0
```
| Method                     | VectorType     | Mean        | Error     | StdDev    | Median      |
|--------------------------- |--------------- |------------:|----------:|----------:|------------:|
| **'Our Base58 Encode'**        | **BitcoinAddress** |    **519.2 ns** |   **1.99 ns** |   **1.76 ns** |    **519.2 ns** |
| 'SimpleBase Base58 Encode' | BitcoinAddress |    770.1 ns |   4.93 ns |   4.61 ns |    769.8 ns |
| 'Our Base58 Decode'        | BitcoinAddress |    187.5 ns |   2.06 ns |   1.93 ns |    187.8 ns |
| 'SimpleBase Base58 Decode' | BitcoinAddress |    502.1 ns |   6.95 ns |   5.43 ns |    502.4 ns |
|                            |                |             |           |           |             |
| **'Our Base58 Encode'**        | **SolanaAddress**  |  **1,375.9 ns** |  **36.83 ns** | **108.59 ns** |  **1,410.7 ns** |
| 'SimpleBase Base58 Encode' | SolanaAddress  |  2,546.5 ns | 112.91 ns | 329.36 ns |  2,661.5 ns |
| 'Our Base58 Decode'        | SolanaAddress  |    536.5 ns |  24.89 ns |  73.39 ns |    564.3 ns |
| 'SimpleBase Base58 Decode' | SolanaAddress  |  1,210.2 ns |  31.14 ns |  90.33 ns |  1,236.8 ns |
|                            |                |             |           |           |             |
| **'Our Base58 Encode'**        | **SolanaTx**       |  **4,185.4 ns** |  **48.51 ns** |  **37.87 ns** |  **4,173.0 ns** |
| 'SimpleBase Base58 Encode' | SolanaTx       | 10,844.0 ns | 337.01 ns | 988.40 ns | 11,158.2 ns |
| 'Our Base58 Decode'        | SolanaTx       |  2,159.0 ns | 116.99 ns | 344.95 ns |  2,294.0 ns |
| 'SimpleBase Base58 Decode' | SolanaTx       |  5,357.9 ns | 177.28 ns | 494.18 ns |  5,506.1 ns |
|                            |                |             |           |           |             |
| **'Our Base58 Encode'**        | **IPFSHash**       |  **1,285.9 ns** |  **80.51 ns** | **237.37 ns** |  **1,081.6 ns** |
| 'SimpleBase Base58 Encode' | IPFSHash       |  1,654.3 ns |   4.14 ns |   3.88 ns |  1,653.9 ns |
| 'Our Base58 Decode'        | IPFSHash       |    347.0 ns |   1.19 ns |   0.99 ns |    347.0 ns |
| 'SimpleBase Base58 Decode' | IPFSHash       |    883.4 ns |  16.93 ns |  15.01 ns |    883.2 ns |
|                            |                |             |           |           |             |
| **'Our Base58 Encode'**        | **MoneroAddress**  |  **4,907.0 ns** |  **13.36 ns** |  **11.84 ns** |  **4,907.9 ns** |
| 'SimpleBase Base58 Encode' | MoneroAddress  |  8,998.7 ns |  25.07 ns |  20.94 ns |  8,998.8 ns |
| 'Our Base58 Decode'        | MoneroAddress  |  1,367.1 ns |   4.38 ns |   3.89 ns |  1,366.5 ns |
| 'SimpleBase Base58 Decode' | MoneroAddress  |  3,809.8 ns |  59.58 ns |  55.73 ns |  3,797.3 ns |


## License

This project is available under the MIT License.