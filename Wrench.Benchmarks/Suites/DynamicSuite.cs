using BenchmarkDotNet.Attributes;
using JNIBinding;
using Wrench.Benchmarks.Harness;
using static Wrench.Benchmarks.Harness.BenchConfig;

namespace Wrench.Benchmarks.Suites;

/// <summary>
/// <c>dynamic</c> calls on a <see cref="GameObject"/>. Uses the plain C# binder: the gate restricted mods get
/// (<c>Security/DynamicBinder</c>) needs a calling type in a mod load context, and only adds cost on a call site's first bind.
/// </summary>
public class DynamicSuite
{
    private JNIEnv _env = null!;
    private GameObject _color = null!;

    [GlobalSetup]
    public void Setup()
    {
        _env = BenchJvm.Start();
        using var batch = new JavaBatch(_env);
        _color = new Zomboid.Java.Core.Color(0.5f, 0.25f, 0.125f, 1f).AsGameObject().Keep();
    }

    [GlobalCleanup]
    public void Cleanup() => _color.Dispose();

    [Benchmark(Baseline = true, OperationsPerInvoke = Ops, Description = "dynamic color.getR()")]
    public double GetR()
    {
        using var batch = new JavaBatch(_env);
        dynamic color = _color;
        var sum = 0.0;
        for (var i = 0; i < Ops; i++)
        {
            double r = color.getR();
            sum += r;
        }
        return sum;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "dynamic color.darker(0.5)")]
    public object Darker()
    {
        using var batch = new JavaBatch(_env);
        dynamic color = _color;
        object result = null!;
        for (var i = 0; i < Ops; i++)
            result = color.darker(0.5);
        return result;
    }
}
