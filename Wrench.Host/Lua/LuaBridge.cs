using System.Collections;
using JNIBinding;
using JNIBinding.Bindings.Native.Types;
using JNIBinding.Exceptions;

namespace Wrench.Host.Lua;

/// <summary>Implements the mod-facing Lua API on top of the agent's <c>io.pznet.wrench.WrenchLua</c>.</summary>
internal sealed unsafe class LuaBridge : ILuaHost
{
    public static readonly LuaBridge Instance = new();

    private static _JClass _wrenchLua, _object, _system;
    private static _JMethodID _getGlobal, _setGlobal, _index, _setIndex, _isTable, _newTable, _call, _callMethod, _function,
        _identityHashCode;

    public static void Initialize(JavaRuntime runtime)
    {
        var env = runtime.Env;
        _wrenchLua = env.NewGlobalRef(runtime.FindSystemClass("io.pznet.wrench.WrenchLua").Value).AsClass();
        var objectClass = env.FindClass("java/lang/Object");
        _object = env.NewGlobalRef(objectClass).AsClass();
        env.DeleteLocalRef(objectClass);
        var systemClass = env.FindClass("java/lang/System");
        _system = env.NewGlobalRef(systemClass).AsClass();
        env.DeleteLocalRef(systemClass);
        _identityHashCode = env.GetStaticMethodId(_system, "identityHashCode", "(Ljava/lang/Object;)I");

        _getGlobal = Static(env, "getGlobal", "(Ljava/lang/String;)Ljava/lang/Object;");
        _setGlobal = Static(env, "setGlobal", "(Ljava/lang/String;Ljava/lang/Object;)V");
        _index = Static(env, "index", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;");
        _setIndex = Static(env, "setIndex", "(Ljava/lang/Object;Ljava/lang/Object;Ljava/lang/Object;)V");
        _isTable = Static(env, "isTable", "(Ljava/lang/Object;)Z");
        _newTable = Static(env, "newTable", "()Ljava/lang/Object;");
        _call = Static(env, "call", "(Ljava/lang/Object;[Ljava/lang/Object;)[Ljava/lang/Object;");
        _callMethod = Static(env, "callMethod", "(Ljava/lang/Object;Ljava/lang/String;[Ljava/lang/Object;)[Ljava/lang/Object;");
        _function = Static(env, "function", "(J)Ljava/lang/Object;");
    }

    public ModInstance? CurrentMod => LuaScope.Current.Mod;

    public object? GetGlobal(string name)
    {
        var scope = LuaScope.Current;
        using var temps = new Temps(scope.Env);
        var result = Guard(() => CallObject(scope.Env, _getGlobal, temps.String(name)));
        return JavaValues.ToDotnet(scope.Env, result, scope);
    }

    public void SetGlobal(ModInstance mod, string name, object? value)
    {
        var scope = LuaScope.Current;
        using var temps = new Temps(scope.Env);
        var jName = temps.String(name);
        var jValue = ToJava(scope, value, mod, temps);
        Guard(() => CallVoid(scope.Env, _setGlobal, jName, jValue));
    }

    /// <summary>Sets a global on Wrench's own behalf; values may not contain delegates, which need an owning mod.</summary>
    public void SetHostGlobal(string name, object? value)
    {
        var scope = LuaScope.Current;
        using var temps = new Temps(scope.Env);
        var jName = temps.String(name);
        var jValue = ToJava(scope, value, null, temps);
        Guard(() => CallVoid(scope.Env, _setGlobal, jName, jValue));
    }

    public object? Index(GameObject target, object key)
    {
        var scope = LuaScope.Current;
        using var temps = new Temps(scope.Env);
        var jKey = ToJava(scope, key, null, temps);
        var result = Guard(() => CallObject(scope.Env, _index, new _JObject(target.Handle), jKey));
        return JavaValues.ToDotnet(scope.Env, result, scope);
    }

    public void SetIndex(ModInstance? mod, GameObject target, object key, object? value)
    {
        var scope = LuaScope.Current;
        using var temps = new Temps(scope.Env);
        var jKey = ToJava(scope, key, mod, temps);
        var jValue = ToJava(scope, value, mod, temps);
        Guard(() => CallVoid(scope.Env, _setIndex, new _JObject(target.Handle), jKey, jValue));
    }

    public bool IsTable(GameObject target)
    {
        var env = LuaScope.Current.Env;
        _JValue* args = stackalloc _JValue[] { new _JValue(new _JObject(target.Handle)) };
        return env.CallStaticBooleanMethod(_wrenchLua, _isTable, args).AsBool();
    }

    public object?[] Call(ModInstance? mod, object function, object?[] args)
    {
        var scope = LuaScope.Current;
        using var temps = new Temps(scope.Env);
        var jFunction = ToJava(scope, function, mod, temps);
        var jArgs = ToJavaArray(scope, args, mod, temps);
        var results = Guard(() => CallObject(scope.Env, _call, jFunction, jArgs));
        return Results(scope, results);
    }

    public object?[] CallMethod(ModInstance? mod, GameObject target, string method, object?[] args)
    {
        var scope = LuaScope.Current;
        using var temps = new Temps(scope.Env);
        var jMethod = temps.String(method);
        var jArgs = ToJavaArray(scope, args, mod, temps);
        var results = Guard(() => CallObject(scope.Env, _callMethod, new _JObject(target.Handle), jMethod, jArgs));
        return Results(scope, results);
    }

    public GameObject NewTable()
    {
        var scope = LuaScope.Current;
        return new GameObject(scope, Guard(() => CallObject(scope.Env, _newTable)).Value);
    }

    public GameObject CreateFunction(ModInstance mod, Delegate function)
    {
        var scope = LuaScope.Current;
        return new GameObject(scope, NewFunction(scope.Env, mod, function).Value);
    }

    public GameObject Keep(GameObject target)
    {
        var env = LuaScope.Current.Env;
        return new GameObject(new KeptScope(), env.NewGlobalRef(new _JObject(target.Handle)).Value);
    }

    public bool SameObject(GameObject a, GameObject b) =>
        LuaScope.Current.Env.IsSameObject(new _JObject(a.Handle), new _JObject(b.Handle));

    public int IdentityHash(GameObject target)
    {
        var env = LuaScope.Current.Env;
        _JValue* args = stackalloc _JValue[] { new _JValue(new _JObject(target.Handle)) };
        return env.CallStaticIntMethod(_system, _identityHashCode, args);
    }

    /// <summary>Converts a .NET value for Lua. New local refs are recorded in <paramref name="temps"/>; game objects are borrowed.</summary>
    public static _JObject ToJava(LuaScope scope, object? value, ModInstance? mod, Temps temps)
    {
        var env = scope.Env;
        switch (value)
        {
            case null:
                return _JObject.Null;
            case GameObject gameObject:
                return new _JObject(gameObject.Handle);
            case IGameObjectView view:
                return new _JObject(view.View.Handle);
            case System.Runtime.CompilerServices.IUnion union:
                return ToJava(scope, union.Value, mod, temps);
            case string or bool:
                return temps.Add(JavaBoxing.Box(env, value));
            case char c:
                return temps.String(c.ToString());
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                // Lua has one number type; Kahlua converts it back to whatever a Java method parameter needs.
                return temps.Add(JavaBoxing.Box(env, Convert.ToDouble(value)));
            case Delegate function:
                // Owned by the calling mod; with neither a mod nor a mod scope it is one of Wrench's own functions.
                return temps.Add(NewFunction(env, mod ?? scope.Mod, function));
            case IDictionary dictionary:
            {
                var table = temps.Add(Guard(() => CallObject(env, _newTable)));
                foreach (DictionaryEntry entry in dictionary)
                {
                    var key = ToJava(scope, entry.Key, mod, temps);
                    var item = ToJava(scope, entry.Value, mod, temps);
                    Guard(() => CallVoid(env, _setIndex, table, key, item));
                }
                return table;
            }
            case IEnumerable sequence:
            {
                var table = temps.Add(Guard(() => CallObject(env, _newTable)));
                var index = 1;
                foreach (var element in sequence)
                {
                    var key = temps.Add(JavaBoxing.Box(env, (double)index++));
                    var item = ToJava(scope, element, mod, temps);
                    Guard(() => CallVoid(env, _setIndex, table, key, item));
                }
                return table;
            }
            default:
                throw new ArgumentException($"{value.GetType()} can't be passed to Lua", nameof(value));
        }
    }

    /// <summary>Builds a Java <c>Object[]</c> for <paramref name="values"/>; the array itself is not recorded as a temp.</summary>
    public static _JObjectArray NewResultArray(LuaScope scope, object?[] values, ModInstance? mod)
    {
        using var temps = new Temps(scope.Env);
        return BuildArray(scope, values, mod, temps);
    }

    private static _JObject ToJavaArray(LuaScope scope, object?[] values, ModInstance? mod, Temps temps) =>
        temps.Add(new _JObject(BuildArray(scope, values, mod, temps).Value));

    private static _JObjectArray BuildArray(LuaScope scope, object?[] values, ModInstance? mod, Temps temps)
    {
        var env = scope.Env;
        var array = env.NewObjectArray(values.Length, _object, _JObject.Null);
        for (var i = 0; i < values.Length; i++)
            env.SetObjectArrayElement(array, i, ToJava(scope, values[i], mod, temps));
        return array;
    }

    private static _JObject NewFunction(JNIEnv env, ModInstance? mod, Delegate function)
    {
        var id = LuaFunctions.Register(mod, function);
        _JValue* args = stackalloc _JValue[] { new _JValue(id) };
        return env.CallStaticObjectMethod(_wrenchLua, _function, args);
    }

    private static object?[] Results(LuaScope scope, _JObject results)
    {
        var values = JavaValues.ToDotnet(scope.Env, new _JObjectArray(results.Value), scope);
        scope.Env.DeleteLocalRef(results);
        return values;
    }

    private static _JObject CallObject(JNIEnv env, _JMethodID method, params _JObject[] args)
    {
        var jArgs = stackalloc _JValue[Math.Max(args.Length, 1)];
        for (var i = 0; i < args.Length; i++)
            jArgs[i] = new _JValue(args[i]);
        return env.CallStaticObjectMethod(_wrenchLua, method, jArgs);
    }

    private static void CallVoid(JNIEnv env, _JMethodID method, params _JObject[] args)
    {
        var jArgs = stackalloc _JValue[Math.Max(args.Length, 1)];
        for (var i = 0; i < args.Length; i++)
            jArgs[i] = new _JValue(args[i]);
        env.CallStaticVoidMethod(_wrenchLua, method, jArgs);
    }

    // WrenchLua reports Lua errors as LuaError; surface those to mods as LuaException with just the Lua message.
    private static T Guard<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (JavaThrowable e)
        {
            throw ToLuaException(e);
        }
    }

    private static void Guard(Action call) => Guard(() =>
    {
        call();
        return 0;
    });

    private static LuaException ToLuaException(JavaThrowable e)
    {
        const string marker = "WrenchLua$LuaError: ";
        var message = e.Message;
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        var text = start >= 0 ? message[(start + marker.Length)..] : message.Replace("Surfaced Java Exception: ", "");
        var end = text.IndexOfAny(['\r', '\n']);
        return new LuaException(end >= 0 ? text[..end] : text);
    }

    private static _JMethodID Static(JNIEnv env, string name, string signature) =>
        env.GetStaticMethodId(_wrenchLua, name, signature);

    /// <summary>Backs <see cref="GameObject.Keep"/>: a JNI global ref, valid until disposed.</summary>
    private sealed class KeptScope : IGameObjectScope
    {
        public bool IsActive { get; private set; } = true;

        public int ThreadId { get; } = Environment.CurrentManagedThreadId;

        public string GetTypeName(nint handle) => JavaBoxing.GetClassName(LuaScope.Current.Env, new _JObject(handle));

        public string Describe(nint handle) => JavaBoxing.ToJavaString(LuaScope.Current.Env, new _JObject(handle));

        public void Release(nint handle)
        {
            if (!IsActive)
                return;
            IsActive = false;
            Entry.CurrentEnv().DeleteGlobalRef(new _JObject(handle));
        }
    }

    /// <summary>Local refs created while converting arguments, released once the call is done.</summary>
    internal sealed class Temps(JNIEnv env) : IDisposable
    {
        private readonly List<_JObject> _refs = [];

        public _JObject Add(_JObject obj)
        {
            if (!obj.IsNull)
                _refs.Add(obj);
            return obj;
        }

        public _JObject String(string value) => Add((_JObject)env.AsJString(value));

        public void Dispose()
        {
            foreach (var obj in _refs)
                env.DeleteLocalRef(obj);
        }
    }
}
