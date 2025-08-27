using BenchmarkDotNet.Running;
using Base58Encoding.Benchmarks;

// Uncomment the benchmark you want to run
//BenchmarkRunner.Run<Base58ComparisonBenchmark>();
//BenchmarkRunner.Run<CountLeadingZerosBenchmark>();
BenchmarkRunner.Run<BoundsCheckComparisonBenchmark>();
