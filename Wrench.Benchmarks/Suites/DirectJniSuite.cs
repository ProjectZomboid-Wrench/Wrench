using BenchmarkDotNet.Attributes;
using JNIBinding;
using JNIBinding.Bindings.Native.Types;
using Wrench.Benchmarks.Harness;
using Wrench.Host.Lua;
using Zomboid.Java;
using Zomboid.Java.Core;
using static Wrench.Benchmarks.Harness.BenchConfig;

namespace Wrench.Benchmarks.Suites;

/// <summary>
/// The direct-JNI pilot: the same two typed calls through Kahlua (today's generated wrappers) and straight through
/// JNI with a cached method id, shaped as a generated direct wrapper would be.
/// </summary>
public class DirectJniSuite
{
    private JNIEnv _env = null!;
    private GameObject _kept = null!;
    private Color _color = null!;
    private _JMethodID _getR, _darker;

    [GlobalSetup]
    public void Setup()
    {
        _env = BenchJvm.Start();
        using var batch = new JavaBatch(_env);
        _kept = new Color(0.5f, 0.25f, 0.125f, 1f).AsGameObject().Keep();
        _color = _kept.As<Color>();
        _getR = DirectJava.Method("zombie.core.Color", "getR", "()F");
        _darker = DirectJava.Method("zombie.core.Color", "darker", "(F)Lzombie/core/Color;");
    }

    [GlobalCleanup]
    public void Cleanup() => _kept.Dispose();

    [Benchmark(Baseline = true, OperationsPerInvoke = Ops, Description = "color.GetR() through Kahlua")]
    public float KahluaGetR()
    {
        using var batch = new JavaBatch(_env);
        var sum = 0f;
        for (var i = 0; i < Ops; i++)
            sum += _color.GetR();
        return sum;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "color.GetR() direct")]
    public float DirectGetR()
    {
        using var batch = new JavaBatch(_env);
        var sum = 0f;
        for (var i = 0; i < Ops; i++)
            sum += DirectJava.CallFloat(_kept, _getR);
        return sum;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "color.Darker(0.5f) through Kahlua")]
    public Color KahluaDarker()
    {
        using var batch = new JavaBatch(_env);
        Color result = null!;
        for (var i = 0; i < Ops; i++)
            result = _color.Darker(0.5f);
        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "color.Darker(0.5f) direct, wrapped by exact class")]
    public Color DirectDarker()
    {
        using var batch = new JavaBatch(_env);
        Color result = null!;
        for (var i = 0; i < Ops; i++)
            result = DirectJava.CallObject(_kept, _darker, 0.5f)!.As<Color>();
        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "color.Darker(0.5f) direct, unwrapped")]
    public GameObject DirectDarkerHandle()
    {
        using var batch = new JavaBatch(_env);
        GameObject result = null!;
        for (var i = 0; i < Ops; i++)
            result = DirectJava.CallObject(_kept, _darker, 0.5f)!;
        return result;
    }
}
