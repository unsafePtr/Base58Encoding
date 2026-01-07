using Base58Encoding.Benchmarks;

using BenchmarkDotNet.Running;

BenchmarkRunner.Run<Base58ComparisonBenchmark>();
//BenchmarkRunner.Run<FastVsRegularEncodeBenchmark>();
//BenchmarkRunner.Run<BoundsCheckComparisonBenchmark>();
