using BenchmarkDotNet.Running;
using Wrench.Benchmarks.Harness;

BenchmarkSwitcher.FromAssembly(typeof(BenchConfig).Assembly).Run(args, BenchConfig.Create());
