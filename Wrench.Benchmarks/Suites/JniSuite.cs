using BenchmarkDotNet.Attributes;
using JNIBinding;
using JNIBinding.Bindings.Native.Types;
using Wrench.Benchmarks.Harness;
using Wrench.Host.Lua;
using static Wrench.Benchmarks.Harness.BenchConfig;

namespace Wrench.Benchmarks.Suites;

/// <summary>Single JNI operations, the building blocks every Wrench call is made of.</summary>
public unsafe class JniSuite
{
    private JNIEnv _env = null!;
    private _JMethodID _nop;
    private delegate* unmanaged<IntPtr, IntPtr, IntPtr, _JValue*, int> _callStaticIntMethodA;
    private _JObject _boxedDouble, _color;

    [GlobalSetup]
    public void Setup()
    {
        _env = BenchJvm.Start();
        _nop = _env.GetStaticMethodId(BenchJvm.Fixtures, "nop", "()I");
        _callStaticIntMethodA = (delegate* unmanaged<IntPtr, IntPtr, IntPtr, _JValue*, int>)_env.FuncHandle->CallStaticIntMethodA;
        var boxed = JavaBoxing.Box(_env, 1.5);
        _boxedDouble = _env.NewGlobalRef(boxed);
        _env.DeleteLocalRef(boxed);
        _color = BenchJvm.NewColor(_env);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _env.DeleteGlobalRef(_boxedDouble);
        _env.DeleteGlobalRef(_color);
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = Ops, Description = "static int call, raw function pointer")]
    public int StaticCallFunctionPointer()
    {
        var sum = 0;
        for (var i = 0; i < Ops; i++)
            sum += _callStaticIntMethodA(_env.EnvHandle.Value, BenchJvm.Fixtures.Value, _nop.Value, null);
        return sum;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "static int call, Functions field")]
    public int StaticCallBridge()
    {
        var sum = 0;
        for (var i = 0; i < Ops; i++)
            sum += _env.Functions.CallStaticIntMethod(_env.EnvHandle.Value, BenchJvm.Fixtures.Value, _nop.Value, null);
        return sum;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "static int call, JNIEnv (with exception check)")]
    public int StaticCallJniEnv()
    {
        var sum = 0;
        for (var i = 0; i < Ops; i++)
            sum += _env.CallStaticIntMethod(BenchJvm.Fixtures, _nop, null);
        return sum;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "ExceptionCheck")]
    public bool ExceptionCheck()
    {
        var any = false;
        for (var i = 0; i < Ops; i++)
            any |= _env.CheckJavaExceptionAndThrow();
        return any;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "NewString + DeleteLocalRef")]
    public void NewString()
    {
        for (var i = 0; i < Ops; i++)
            _env.DeleteLocalRef((_JObject)_env.AsJString("getR"));
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "box double + DeleteLocalRef")]
    public void BoxDouble()
    {
        for (var i = 0; i < Ops; i++)
            _env.DeleteLocalRef(JavaBoxing.Box(_env, 1.5));
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "TryUnbox, Double")]
    public object? UnboxDouble()
    {
        object? value = null;
        for (var i = 0; i < Ops; i++)
            JavaBoxing.TryUnbox(_env, _boxedDouble, out value);
        return value;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "TryUnbox, game object (miss)")]
    public bool UnboxObject()
    {
        var any = false;
        for (var i = 0; i < Ops; i++)
            any |= JavaBoxing.TryUnbox(_env, _color, out _);
        return any;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "class name, uncached")]
    public string ClassName()
    {
        var name = "";
        for (var i = 0; i < Ops; i++)
            name = JavaBoxing.GetClassName(_env, _color);
        return name;
    }

    [Benchmark(OperationsPerInvoke = Ops, Description = "class name (GameObject.TypeName)")]
    public string CachedClassName()
    {
        var name = "";
        for (var i = 0; i < Ops; i++)
            name = JavaClassNames.Of(_env, _color);
        return name;
    }
}
