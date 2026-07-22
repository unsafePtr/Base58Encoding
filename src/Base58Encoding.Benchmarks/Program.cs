using Base58Encoding.Benchmarks;

using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(EndToEndEncodeBenchmark).Assembly).Run(args);
