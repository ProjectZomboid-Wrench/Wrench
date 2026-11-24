using BenchmarkDotNet.Attributes;
using JNIBinding;
using Wrench.Benchmarks.Harness;
using Wrench.Host.Lua;
using Zomboid.Java;
using Zomboid.Java.Core;
using static Wrench.Benchmarks.Harness.BenchConfig;

namespace Wrench.Benchmarks.Suites;

/// <summary>The generated wrappers (<c>Wrench.Game.Java</c>), as mods call them.</summary>
public class TypedApiSuite
{
    private const int ListSize = 100;

    private JNIEnv _env = null!;
    private GameObject _keptColor = null!, _keptList = null!;
    private Color _color = null!;

    [GlobalSetup]
    public void Setup()
    {
        _env = BenchJvm.Start();
        using var batch = new JavaBatch(_env);
        _keptColor = new Color(0.5f, 0.25f, 0.125f, 1f).AsGameObject().Keep();
        _color = _keptColor.As<Color>();
        var list = new JavaList<Color>();
        for (var i = 0; i < ListSize; i++)
            list.Add(new Color(i / (float)ListSize, 0f, 0f, 1f));
        _keptList = list.AsGameObject().Keep();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _keptColor.Dispose();
        _keptList.Dispose();
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = Ops, Description = "color.GetR()")]
    public float GetR()
    {
        using var batch = new JavaBatch(_env);
        var sum = 0f;
        for (var i = 0; i < Ops; i++)
            sum += _color.GetR();
        return sum;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "color.Darker(0.5f) -> wrapper")]
    public Color Darker()
    {
        using var batch = new JavaBatch(_env);
        Color result = null!;
        for (var i = 0; i < Ops; i++)
            result = _color.Darker(0.5f);
        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "Color.HSBtoRGB(0.5f, 0.5f, 0.5f) -> wrapper")]
    public Color HsbToRgb()
    {
        using var batch = new JavaBatch(_env);
        Color result = null!;
        for (var i = 0; i < Ops; i++)
            result = Color.HSBtoRGB(0.5f, 0.5f, 0.5f);
        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "new Color(0.5f, 0.25f, 0.125f, 1f)")]
    public Color NewColor()
    {
        using var batch = new JavaBatch(_env);
        Color result = null!;
        for (var i = 0; i < Ops; i++)
            result = new Color(0.5f, 0.25f, 0.125f, 1f);
        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "color.ChangeHSBValue(1, 1, 1)")]
    public void ChangeHsbValue()
    {
        using var batch = new JavaBatch(_env);
        for (var i = 0; i < Ops; i++)
            _color.ChangeHSBValue(1f, 1f, 1f);
    }

    // A JavaList can't be rebuilt over a kept ref through the public API, so each batch fills a new one: one call per batch.
    private static JavaList<Color> CopyOf(GameObject source)
    {
        var list = new JavaList<Color>();
        LuaBridge.Instance.CallMethod(null, list.AsGameObject(), "addAll", [source]);
        return list;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "list.Count")]
    public int ListCount()
    {
        using var batch = new JavaBatch(_env);
        var list = CopyOf(_keptList);
        var sum = 0;
        for (var i = 0; i < Ops; i++)
            sum += list.Count;
        return sum;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "list[i] -> wrapper")]
    public Color ListIndexer()
    {
        using var batch = new JavaBatch(_env);
        var list = CopyOf(_keptList);
        Color result = null!;
        for (var i = 0; i < Ops; i++)
            result = list[i % ListSize];
        return result;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "foreach over list, per element")]
    public Color ListForeach()
    {
        using var batch = new JavaBatch(_env);
        var list = CopyOf(_keptList);
        Color result = null!;
        for (var pass = 0; pass < Ops / ListSize; pass++)
            foreach (var color in list)
                result = color;
        return result;
    }
}
