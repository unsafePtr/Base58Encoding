# Base58 Encoding Library

[![NuGet](https://img.shields.io/nuget/v/Base58Encoding.svg)](https://www.nuget.org/packages/Base58Encoding/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Base58Encoding.svg)](https://www.nuget.org/packages/Base58Encoding/)

A .NET 9.0 Base58 encoding and decoding library with support for multiple alphabet variants.

## Features

- **Multiple Alphabets**: Built-in support for Bitcoin(IFPS/Sui), Ripple, and Flickr alphabets
- **Memory Efficient**: Uses span operations to minimize allocations
- **Type Safe**: Leverages ReadOnlySpan and ReadOnlyMemory for safe operations
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
BenchmarkDotNet v0.13.12, Windows 11 (10.0.26100.4946)
13th Gen Intel Core i7-13700KF, 1 CPU, 24 logical and 16 physical cores
.NET SDK 9.0.304
  [Host]   : .NET 9.0.8 (9.0.825.36511), X64 RyuJIT AVX2
  .NET 9.0 : .NET 9.0.8 (9.0.825.36511), X64 RyuJIT AVX2

Job=.NET 9.0  Runtime=.NET 9.0  
```
| Method                     | DataSize | Mean        | Error      | StdDev       | Median      | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |--------- |------------:|-----------:|-------------:|------------:|------:|--------:|-------:|----------:|------------:|
| **&#39;Our Base58 Encode&#39;**        | **8**        |    **51.62 ns** |   **0.557 ns** |     **0.521 ns** |    **51.62 ns** |  **1.81** |    **0.04** | **0.0030** |      **48 B** |        **1.50** |
| &#39;SimpleBase Base58 Encode&#39; | 8        |    55.59 ns |   0.251 ns |     0.234 ns |    55.59 ns |  1.95 |    0.03 | 0.0030 |      48 B |        1.50 |
| &#39;NBitcoin Base58 Encode&#39;   | 8        |    63.25 ns |   1.229 ns |     1.366 ns |    63.07 ns |  2.23 |    0.05 | 0.0030 |      48 B |        1.50 |
| &#39;Our Base58 Decode&#39;        | 8        |    28.57 ns |   0.528 ns |     0.494 ns |    28.54 ns |  1.00 |    0.00 | 0.0020 |      32 B |        1.00 |
| &#39;SimpleBase Base58 Decode&#39; | 8        |    62.70 ns |   0.597 ns |     0.558 ns |    62.45 ns |  2.20 |    0.04 | 0.0020 |      32 B |        1.00 |
|                            |          |             |            |              |             |       |         |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **13**       |   **125.03 ns** |   **1.721 ns** |     **1.525 ns** |   **124.68 ns** |  **2.11** |    **0.03** | **0.0041** |      **64 B** |        **1.60** |
| &#39;SimpleBase Base58 Encode&#39; | 13       |   143.07 ns |   1.342 ns |     1.256 ns |   143.22 ns |  2.41 |    0.05 | 0.0041 |      64 B |        1.60 |
| &#39;NBitcoin Base58 Encode&#39;   | 13       |   152.49 ns |   1.776 ns |     1.661 ns |   153.13 ns |  2.57 |    0.04 | 0.0041 |      64 B |        1.60 |
| &#39;Our Base58 Decode&#39;        | 13       |    59.34 ns |   0.876 ns |     0.776 ns |    59.35 ns |  1.00 |    0.00 | 0.0025 |      40 B |        1.00 |
| &#39;SimpleBase Base58 Decode&#39; | 13       |   158.13 ns |   0.529 ns |     0.495 ns |   158.19 ns |  2.66 |    0.04 | 0.0024 |      40 B |        1.00 |
|                            |          |             |            |              |             |       |         |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **32**       |   **900.00 ns** |  **13.132 ns** |    **11.641 ns** |   **899.01 ns** |  **3.05** |    **0.04** | **0.0067** |     **112 B** |        **2.00** |
| &#39;SimpleBase Base58 Encode&#39; | 32       |   956.71 ns |   6.189 ns |     5.789 ns |   955.69 ns |  3.24 |    0.02 | 0.0057 |     112 B |        2.00 |
| &#39;NBitcoin Base58 Encode&#39;   | 32       | 1,215.07 ns |  11.678 ns |    10.923 ns | 1,216.22 ns |  4.11 |    0.04 | 0.0057 |     112 B |        2.00 |
| &#39;Our Base58 Decode&#39;        | 32       |   295.31 ns |   1.387 ns |     1.297 ns |   295.56 ns |  1.00 |    0.00 | 0.0033 |      56 B |        1.00 |
| &#39;SimpleBase Base58 Decode&#39; | 32       |   693.05 ns |   5.673 ns |     5.306 ns |   691.92 ns |  2.35 |    0.02 | 0.0029 |      56 B |        1.00 |
|                            |          |             |            |              |             |       |         |        |           |             |
| **&#39;Our Base58 Encode&#39;**        | **69**       | **4,515.29 ns** |  **18.892 ns** |    **15.776 ns** | **4,519.64 ns** |  **2.41** |    **0.82** | **0.0076** |     **216 B** |        **2.25** |
| &#39;SimpleBase Base58 Encode&#39; | 69       | 6,924.91 ns | 137.444 ns |   251.323 ns | 7,064.97 ns |  3.08 |    0.74 | 0.0076 |     216 B |        2.25 |
| &#39;NBitcoin Base58 Encode&#39;   | 69       | 7,536.47 ns | 150.542 ns |   282.753 ns | 7,613.02 ns |  3.36 |    0.79 | 0.0076 |     216 B |        2.25 |
| &#39;Our Base58 Decode&#39;        | 69       | 2,403.76 ns |  87.230 ns |   257.200 ns | 2,503.34 ns |  1.00 |    0.00 | 0.0057 |      96 B |        1.00 |
| &#39;SimpleBase Base58 Decode&#39; | 69       | 4,697.46 ns | 447.955 ns | 1,320.805 ns | 3,896.45 ns |  1.98 |    0.58 |      - |      96 B |        1.00 |


## License

This project is available under the MIT License.