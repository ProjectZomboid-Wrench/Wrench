using BenchmarkDotNet.Attributes;
using JNIBinding;
using JNIBinding.Bindings.Native.Types;
using Wrench.Benchmarks.Harness;
using Wrench.Host.Events;
using Wrench.Host.Lua;
using Zomboid.Java;
using Zomboid.Java.Core;
using static Wrench.Benchmarks.Harness.BenchConfig;

namespace Wrench.Benchmarks.Suites;

/// <summary>
/// Calls into .NET, driven by a Java loop: Lua calling a .NET function (through Kahlua's pcall) and Lua events
/// reaching mod handlers (through <c>WrenchEvents.dispatch</c>, as the <c>Event.trigger</c> advice calls it).
/// </summary>
public unsafe class JavaToDotnetSuite
{
    private readonly BenchMod _mod = new();
    private readonly List<IDisposable> _registrations = [];
    private JNIEnv _env = null!;
    private _JMethodID _callGlobal, _fireEvent, _fireSquares;
    private static readonly object?[] NoArgs = [];
    private int _solid;
    private _JObject _color, _number;
    private readonly Dictionary<string, _JObject> _names = [];

    [GlobalSetup]
    public void Setup()
    {
        _env = BenchJvm.Start();
        _callGlobal = _env.GetStaticMethodId(BenchJvm.Fixtures, "callGlobal", "(Ljava/lang/String;Ljava/lang/Object;I)V");
        _fireEvent = _env.GetStaticMethodId(BenchJvm.Fixtures, "fireEvent", "(Ljava/lang/String;Ljava/lang/Object;I)V");
        _fireSquares = _env.GetStaticMethodId(BenchJvm.Fixtures, "fireSquares", "(Ljava/lang/String;Z)V");
        var number = JavaBoxing.Box(_env, 0.5);
        _number = _env.NewGlobalRef(number);
        _env.DeleteLocalRef(number);

        _color = BenchJvm.NewColor(_env);
        using var batch = new JavaBatch(_env, _mod);
        Function("benchNumber", (Func<double, double>)(x => x * 2));
        Function("benchTyped", (Action<Color>)(_ => { }));
        Function("benchTypedCall", (Func<Color, float>)(color => color.GetR()));
        Event("BenchEmpty", _ => { });
        Event("BenchTypedCall", e => ((GameObject)e[0]!).As<Color>().GetR());
        Event("BenchSquare", e =>
        {
            if (LuaBridge.Instance.CallMethod(null, (GameObject)e[0]!, "isSolidTrans", NoArgs)[0] is true)
                _solid++;
        });
        Batch("BenchSquareBatchEach", batch =>
        {
            foreach (var square in batch.Argument(0))
            {
                if (LuaBridge.Instance.CallMethod(null, square!, "isSolidTrans", NoArgs)[0] is true)
                    _solid++;
            }
        });
        Batch("BenchSquareBatchWhere", batch => _solid += batch.Argument(0).Where("isSolidTrans").Count);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var registration in _registrations)
            registration.Dispose();
        foreach (var name in _names.Values)
            _env.DeleteGlobalRef(name);
        _env.DeleteGlobalRef(_color);
        _env.DeleteGlobalRef(_number);
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = Ops, Description = "Lua -> .NET fn(number) -> number")]
    public void FunctionNumber() => Drive(_callGlobal, "benchNumber", _number);

    [Benchmark(OperationsPerInvoke = Ops, Description = "Lua -> .NET fn(Color)")]
    public void FunctionTyped() => Drive(_callGlobal, "benchTyped", _color);

    [Benchmark(OperationsPerInvoke = Ops, Description = "Lua -> .NET fn(Color) => color.GetR()")]
    public void FunctionTypedCall() => Drive(_callGlobal, "benchTypedCall", _color);

    [Benchmark(OperationsPerInvoke = Ops, Description = "event -> empty mod handler")]
    public void EventEmpty() => Drive(_fireEvent, "BenchEmpty", _color);

    [Benchmark(OperationsPerInvoke = Ops, Description = "event -> handler calling arg.GetR()")]
    public void EventTypedCall() => Drive(_fireEvent, "BenchTypedCall", _color);

    [Benchmark(OperationsPerInvoke = Ops, Description = "square per event, isSolidTrans from C#")]
    public void SquarePerEvent() => FireSquares("BenchSquare", batched: false);

    [Benchmark(OperationsPerInvoke = Ops, Description = "square batch, isSolidTrans from C# per square")]
    public void SquareBatchEach() => FireSquares("BenchSquareBatchEach", batched: true);

    [Benchmark(OperationsPerInvoke = Ops, Description = "square batch, Where(isSolidTrans) in Java")]
    public void SquareBatchWhere() => FireSquares("BenchSquareBatchWhere", batched: true);

    private void FireSquares(string name, bool batched)
    {
        _JValue* args = stackalloc _JValue[] { new _JValue(_names[name]), new _JValue(batched ? _JBoolean.True : _JBoolean.False) };
        _env.CallStaticVoidMethod(BenchJvm.Fixtures, _fireSquares, args);
    }

    private void Batch(string name, Action<LuaEventBatch> handler)
    {
        _registrations.Add(LuaEventRouter.RegisterBatch(_mod, name, handler));
        AddName(name);
    }

    private void Drive(_JMethodID method, string name, _JObject arg)
    {
        _JValue* args = stackalloc _JValue[] { new _JValue(_names[name]), new _JValue(arg), new _JValue(Ops) };
        _env.CallStaticVoidMethod(BenchJvm.Fixtures, method, args);
    }

    private void Function(string name, Delegate function)
    {
        LuaBridge.Instance.SetGlobal(_mod, name, LuaBridge.Instance.CreateFunction(_mod, function));
        AddName(name);
    }

    private void Event(string name, Action<LuaEventArgs> handler)
    {
        _registrations.Add(LuaEventRouter.Register(_mod, name, handler));
        AddName(name);
    }

    private void AddName(string name)
    {
        var local = (_JObject)_env.AsJString(name);
        _names[name] = _env.NewGlobalRef(local);
        _env.DeleteLocalRef(local);
    }
}
