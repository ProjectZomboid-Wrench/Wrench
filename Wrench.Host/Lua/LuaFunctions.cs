using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
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
    private static readonly ConcurrentDictionary<long, BoundFunction> Functions = new();
    private static readonly ConcurrentDictionary<MethodInfo, Func<object?, object?[], object?>> Invokers = new();
    private static readonly Func<object, string> CallOrigin = function => $"the Lua call into {Describe((Delegate)function)}";
    private static readonly Logger Log = Logging.Log.For("Wrench.Host");
    private static long _lastId;

    // Entries are never removed: Lua may keep a function alive indefinitely, and there are few of them.
    public static long Register(ModInstance? mod, Delegate function)
    {
        var id = Interlocked.Increment(ref _lastId);
        Functions[id] = new BoundFunction(mod, function);
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
            using var scope = LuaScope.Enter(jEnv, entry.Mod, entry.Function, CallOrigin);
            var result = entry.Invoke(ValueBuffer.ReadAll(scope, new _JObject(args)));
            if (entry.ReturnsVoid)
            {
                ValueBuffer.WriteNone(jEnv);
                return IntPtr.Zero;
            }
            using var temps = new LuaBridge.Temps(jEnv);
            // Returned to Java as a local ref of this native call, so it isn't released here.
            return ValueBuffer.WriteOne(scope, result, entry.Mod, temps).Value;
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

    private static object? Bind(object? value, Type type, int index)
    {
        if (value is null)
            return type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;
        if (type.IsInstanceOfType(value))
            return value;
        if (value is GameObject || typeof(System.Runtime.CompilerServices.IUnion).IsAssignableFrom(Nullable.GetUnderlyingType(type) ?? type))
            return Zomboid.Lua.LuaConvert.To(value, type);
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (value is IConvertible convertible && typeof(IConvertible).IsAssignableFrom(target))
            return convertible.ToType(target, CultureInfo.InvariantCulture);
        throw new LuaException($"argument #{index + 1}: expected {type.Name}, got {value.GetType().Name}");
    }

    // Calls the delegate's method directly when it can be, which is much cheaper than DynamicInvoke.
    private static Func<object?, object?[], object?>? DirectInvoker(Delegate function)
    {
        var method = function.Method;
        if (function.GetInvocationList().Length != 1 || method.IsStatic != function.Target is null
            || method.GetParameters().Any(p => p.ParameterType.IsByRef || p.ParameterType.IsPointer))
            return null;
        return Invokers.GetOrAdd(method, static method =>
        {
            var target = Expression.Parameter(typeof(object));
            var args = Expression.Parameter(typeof(object?[]));
            var call = Expression.Call(method.IsStatic ? null : Expression.Convert(target, method.DeclaringType!), method,
                method.GetParameters().Select((p, i) =>
                    Expression.Convert(Expression.ArrayIndex(args, Expression.Constant(i)), p.ParameterType)));
            Expression body = method.ReturnType == typeof(void)
                ? Expression.Block(call, Expression.Constant(null))
                : Expression.Convert(call, typeof(object));
            return Expression.Lambda<Func<object?, object?[], object?>>(body, target, args).Compile();
        });
    }

    /// <summary>A registered delegate with what binding Lua arguments to it needs, worked out once.</summary>
    private sealed class BoundFunction
    {
        private readonly Type[] _parameters;
        private readonly bool _takesAllArgs;
        private readonly Func<object?, object?[], object?>? _invoker;

        public BoundFunction(ModInstance? mod, Delegate function)
        {
            Mod = mod;
            Function = function;
            ReturnsVoid = function.Method.ReturnType == typeof(void);
            _parameters = [.. function.Method.GetParameters().Select(p => p.ParameterType)];
            _takesAllArgs = _parameters is [var only] && only == typeof(object[]);
            _invoker = DirectInvoker(function);
        }

        public ModInstance? Mod { get; }

        public Delegate Function { get; }

        public bool ReturnsVoid { get; }

        public object? Invoke(object?[] args)
        {
            object?[] bound;
            if (_takesAllArgs)
                bound = [args];
            else
            {
                bound = new object?[_parameters.Length];
                for (var i = 0; i < _parameters.Length; i++)
                    bound[i] = Bind(i < args.Length ? args[i] : null, _parameters[i], i);
            }
            return _invoker is null ? Function.DynamicInvoke(bound) : _invoker(Function.Target, bound);
        }
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
