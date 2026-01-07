using BenchmarkDotNet.Running;
using Base58Encoding.Benchmarks;

BenchmarkRunner.Run<Base58ComparisonBenchmark>();
//BenchmarkRunner.Run<FastVsRegularEncodeBenchmark>();
//BenchmarkRunner.Run<BoundsCheckComparisonBenchmark>();
