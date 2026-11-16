using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using JNIBinding;
using JNIBinding.Bindings.Native.Types;
using Wrench.Host.Logging;
using Wrench.Host.Mods;

namespace Wrench.Host.Lua;

/// <summary>Delegates exposed to Lua as <c>io.pznet.wrench.DotnetFunction</c>s, keyed by id.</summary>
internal static class LuaFunctions
{
    // A null owner means Wrench's own functions, such as WrenchHost.decide.
    private static readonly ConcurrentDictionary<long, (ModInstance? Mod, Delegate Function)> Functions = new();
    private static readonly Logger Log = Logging.Log.For("Wrench.Host");
    private static long _lastId;

    // Entries are never removed: Lua may keep a function alive indefinitely, and there are few of them.
    public static long Register(ModInstance? mod, Delegate function)
    {
        var id = Interlocked.Increment(ref _lastId);
        Functions[id] = (mod, function);
        return id;
    }

    [UnmanagedCallersOnly]
    internal static IntPtr Invoke(IntPtr env, IntPtr cls, long id, IntPtr args)
    {
        var jEnv = JNIEnv.ForHandle(new _JNIEnv(env));
        if (!Functions.TryGetValue(id, out var entry))
        {
            Entry.ThrowToJava(jEnv, new InvalidOperationException($"Unknown .NET function #{id}"));
            return IntPtr.Zero;
        }

        try
        {
            using var scope = LuaScope.Enter(jEnv, entry.Mod);
            var luaArgs = JavaValues.ToDotnet(jEnv, new _JObjectArray(args), scope);
            var result = InvokeBound(entry.Function, luaArgs);
            if (entry.Function.Method.ReturnType == typeof(void))
                return IntPtr.Zero;
            // The array is returned to Java as a local ref of this native call, so it isn't released here.
            return LuaBridge.NewResultArray(scope, [result], entry.Mod).Value;
        }
        catch (Exception e)
        {
            var error = e is TargetInvocationException { InnerException: { } inner } ? inner : e;
            if (entry.Mod is null)
                Log.Error($"Lua call into {Describe(entry.Function)} failed", error);
            else
                ModHost.Instance.Log(entry.Mod, LogLevel.Error, $"Lua call into {Describe(entry.Function)} failed", error);
            // Raised in Lua as an error from this function, as a failing Lua function would be.
            Entry.ThrowToJava(jEnv, $"{entry.Mod?.Id ?? "Wrench"}: {error.Message}");
            return IntPtr.Zero;
        }
    }

    private static object? InvokeBound(Delegate function, object?[] args)
    {
        var parameters = function.Method.GetParameters();
        if (parameters is [{ ParameterType: var only }] && only == typeof(object[]))
            return function.DynamicInvoke([args]);

        var bound = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            bound[i] = Bind(i < args.Length ? args[i] : null, parameters[i].ParameterType, i);
        return function.DynamicInvoke(bound);
    }

    private static object? Bind(object? value, Type type, int index)
    {
        if (value is null)
            return type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;
        if (type.IsInstanceOfType(value))
            return value;
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (value is IConvertible convertible && typeof(IConvertible).IsAssignableFrom(target))
            return convertible.ToType(target, CultureInfo.InvariantCulture);
        throw new LuaException($"argument #{index + 1}: expected {type.Name}, got {value.GetType().Name}");
    }

    private static string Describe(Delegate function)
    {
        var method = function.Method;
        var type = method.DeclaringType;
        // Lambdas compile to methods like <AddChallenge>b__1_2 on a nested closure class.
        if (method.Name.Contains('<'))
        {
            var owner = type is { IsNested: true, Name: ['<', ..] } ? type.DeclaringType : type;
            return $"a lambda in {owner?.Name}";
        }
        return $"{type?.Name}.{method.Name}";
    }
}
