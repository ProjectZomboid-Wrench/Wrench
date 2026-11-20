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
    private static readonly object?[] TextArg = ["text"];

    private JNIEnv _env = null!;
    private GameObject _color = null!;
    private GameObject _table = null!;
    private int _counter;

    [GlobalSetup]
    public void Setup()
    {
        _env = BenchJvm.Start();
        using var batch = new JavaBatch(_env);
        _color = new Zomboid.Java.Core.Color(0.5f, 0.25f, 0.125f, 1f).AsGameObject().Keep();
        _table = LuaBridge.Instance.NewTable().Keep();
        LuaBridge.Instance.SetIndex(null, _table, "field", 1.0);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _color.Dispose();
        _table.Dispose();
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "table.field (literal key)")]
    public object? TableGet()
    {
        using var batch = new JavaBatch(_env);
        object? result = null;
        for (var i = 0; i < Ops; i++)
            result = LuaBridge.Instance.Index(_table, "field");
        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "table.field = 2 (literal key)")]
    public void TableSet()
    {
        using var batch = new JavaBatch(_env);
        for (var i = 0; i < Ops; i++)
            LuaBridge.Instance.SetIndex(null, _table, "field", 2.0);
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "table[built key] (run-time string)")]
    public object? TableGetBuiltKey()
    {
        using var batch = new JavaBatch(_env);
        object? result = null;
        for (var i = 0; i < Ops; i++)
            result = LuaBridge.Instance.Index(_table, (++_counter & 1) == 0 ? string.Concat("fi", "eld") : string.Concat("fie", "ld"));
        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "color:equals(\"text\") (string argument)")]
    public object? CallStringArg()
    {
        using var batch = new JavaBatch(_env);
        object? result = null;
        for (var i = 0; i < Ops; i++)
            result = LuaBridge.Instance.CallMethod(null, _color, "equals", TextArg)[0];
        return result;
    }

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

    [Benchmark(OperationsPerInvoke = Ops, Description = "Color.HSBtoRGB(...) via GetGlobal + Index + Call")]
    public object? StaticThreeSteps()
    {
        using var batch = new JavaBatch(_env);
        object? result = null;
        for (var i = 0; i < Ops; i++)
        {
            var table = (GameObject)LuaBridge.Instance.GetGlobal("Color")!;
            var function = LuaBridge.Instance.Index(table, "HSBtoRGB")!;
            result = LuaBridge.Instance.Call(null, function, ThreeArgs)[0];
        }
        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "Color.HSBtoRGB(...) via CallStatic")]
    public object? StaticOneStep()
    {
        using var batch = new JavaBatch(_env);
        object? result = null;
        for (var i = 0; i < Ops; i++)
            result = LuaBridge.Instance.CallStatic(null, "Color", "HSBtoRGB", ThreeArgs)[0];
        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "benchGlobal() via GetGlobal + Call")]
    public object? GlobalTwoSteps()
    {
        using var batch = new JavaBatch(_env);
        object? result = null;
        for (var i = 0; i < Ops; i++)
            result = LuaBridge.Instance.Call(null, LuaBridge.Instance.GetGlobal("benchGlobal")!, NoArgs)[0];
        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "benchGlobal() via CallGlobal")]
    public object? GlobalOneStep()
    {
        using var batch = new JavaBatch(_env);
        object? result = null;
        for (var i = 0; i < Ops; i++)
            result = LuaBridge.Instance.CallGlobal(null, "benchGlobal", NoArgs)[0];
        return result;
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
