using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace Wrench.Benchmarks.Harness;

internal static class BenchConfig
{
    /// <summary>Calls per benchmark invocation, so a batch's local frame and scope cost little per call.</summary>
    public const int Ops = 1000;

    // In-process: the JVM lives in this process, and a process can host only one.
    public static IConfig Create() => ManualConfig.Create(DefaultConfig.Instance)
        .AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance))
        .AddDiagnoser(MemoryDiagnoser.Default);
}
