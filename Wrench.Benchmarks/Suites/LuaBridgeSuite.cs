using BenchmarkDotNet.Attributes;
using JNIBinding;
using Wrench.Benchmarks.Harness;
using Wrench.Host.Lua;
using static Wrench.Benchmarks.Harness.BenchConfig;

namespace Wrench.Benchmarks.Suites;

/// <summary>The untyped host calls that the typed API and <c>dynamic</c> are built on.</summary>
public class LuaBridgeSuite
{
    private static readonly object?[] NoArgs = [];
    private static readonly object?[] OneArg = [0.5];
    private static readonly object?[] ThreeArgs = [1.0, 1.0, 1.0];

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

    [Benchmark(Baseline = true, OperationsPerInvoke = Ops, Description = "color:getR() -> number")]
    public object? CallNoArgsNumber()
    {
        using var batch = new JavaBatch(_env);
        object? result = null;
        for (var i = 0; i < Ops; i++)
            result = LuaBridge.Instance.CallMethod(null, _color, "getR", NoArgs)[0];
        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "color:darker(0.5) -> object")]
    public object? CallOneArgObject()
    {
        using var batch = new JavaBatch(_env);
        object? result = null;
        for (var i = 0; i < Ops; i++)
            result = LuaBridge.Instance.CallMethod(null, _color, "darker", OneArg)[0];
        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "color:changeHSBValue(1, 1, 1) -> void")]
    public int CallThreeArgsVoid()
    {
        using var batch = new JavaBatch(_env);
        var count = 0;
        for (var i = 0; i < Ops; i++)
            count += LuaBridge.Instance.CallMethod(null, _color, "changeHSBValue", ThreeArgs).Length;
        return count;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "color[\"getR\"] (index only)")]
    public object? Index()
    {
        using var batch = new JavaBatch(_env);
        object? result = null;
        for (var i = 0; i < Ops; i++)
            result = LuaBridge.Instance.Index(_color, "getR");
        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "GetGlobal(\"Color\")")]
    public object? GetGlobal()
    {
        using var batch = new JavaBatch(_env);
        object? result = null;
        for (var i = 0; i < Ops; i++)
            result = LuaBridge.Instance.GetGlobal("Color");
        return result;
    }
}
