using System.Collections.Concurrent;
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
    private static _JMethodID _getGlobal, _setGlobal, _index, _setIndex, _isTable, _newTable, _invoke, _getStatic, _function,
        _identityHashCode;
    private static _JObject _true, _false;
    // Calls with up to this many arguments use WrenchLua's fixed-arity entry points: no Object[], no boxed numbers.
    private const int MaxDirectArgs = 4;
    private static readonly _JMethodID[] InvokeN = new _JMethodID[MaxDirectArgs + 1];

    // Global refs to short strings (names, keys, literal arguments), so repeats don't make a Java string per use. The
    // cap bounds what run-time strings can pin; past it, and for longer strings, each use makes its own.
    private const int MaxCachedStrings = 4096, MaxCachedLength = 64;
    private static readonly ConcurrentDictionary<string, _JObject> Strings = new(StringComparer.Ordinal);
    private static readonly Lock StringsGate = new();

    // How WrenchLua.invoke finds the function; keep in sync with its modes.
    private enum InvokeMode
    {
        Function, Method, Static, Global, StaticMethod, StaticOptional,
    }

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
        _invoke = Static(env, "invoke", "(ILjava/lang/Object;Ljava/lang/String;[Ljava/lang/Object;)Ljava/lang/Object;");
        _getStatic = Static(env, "getStatic", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/Object;");
        ValueBuffer.Initialize(env, _wrenchLua);
        _function = Static(env, "function", "(J)Ljava/lang/Object;");

        for (var arity = 0; arity <= MaxDirectArgs; arity++)
        {
            var args = arity == 0 ? "" : "I" + string.Concat(Enumerable.Repeat("Ljava/lang/Object;D", arity));
            InvokeN[arity] = Static(env, $"invoke{arity}", $"(ILjava/lang/Object;Ljava/lang/String;{args})Ljava/lang/Object;");
        }

        var booleanClass = env.FindClass("java/lang/Boolean");
        _true = BooleanConstant(env, booleanClass, "TRUE");
        _false = BooleanConstant(env, booleanClass, "FALSE");
        env.DeleteLocalRef(booleanClass);
    }

    private static _JObject BooleanConstant(JNIEnv env, _JClass booleanClass, string name)
    {
        var local = env.GetStaticObjectField(booleanClass, env.GetStaticFieldID(booleanClass, name, "Ljava/lang/Boolean;"));
        var global = env.NewGlobalRef(local);
        env.DeleteLocalRef(local);
        return global;
    }

    public ModInstance? CurrentMod => LuaScope.Current.Mod;

    public object? GetGlobal(string name)
    {
        var scope = LuaScope.Current;
        using var temps = Temps.Rent(scope.Env);
        var jName = JavaString(scope.Env, name, temps);
        var result = CallObject(scope.Env, _getGlobal, jName);
        return ValueBuffer.ReadOne(scope, result);
    }

    public void SetGlobal(ModInstance mod, string name, object? value)
    {
        var scope = LuaScope.Current;
        using var temps = Temps.Rent(scope.Env);
        var jName = JavaString(scope.Env, name, temps);
        var jValue = ToJava(scope, value, mod, temps);
        CallVoid(scope.Env, _setGlobal, jName, jValue);
    }

    /// <summary>Sets a global on Wrench's own behalf; values may not contain delegates, which need an owning mod.</summary>
    public void SetHostGlobal(string name, object? value)
    {
        var scope = LuaScope.Current;
        using var temps = Temps.Rent(scope.Env);
        var jName = JavaString(scope.Env, name, temps);
        var jValue = ToJava(scope, value, null, temps);
        CallVoid(scope.Env, _setGlobal, jName, jValue);
    }

    public object? Index(GameObject target, object key)
    {
        var scope = LuaScope.Current;
        using var temps = Temps.Rent(scope.Env);
        var jKey = ToJava(scope, key, null, temps);
        var result = CallObject(scope.Env, _index, new _JObject(target.Handle), jKey);
        return ValueBuffer.ReadOne(scope, result);
    }

    public void SetIndex(ModInstance? mod, GameObject target, object key, object? value)
    {
        var scope = LuaScope.Current;
        using var temps = Temps.Rent(scope.Env);
        var jKey = ToJava(scope, key, mod, temps);
        var jValue = ToJava(scope, value, mod, temps);
        CallVoid(scope.Env, _setIndex, new _JObject(target.Handle), jKey, jValue);
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
        using var temps = Temps.Rent(scope.Env);
        return Invoke(scope, temps, mod, InvokeMode.Function, ToJava(scope, function, mod, temps), _JObject.Null, args);
    }

    public object?[] CallMethod(ModInstance? mod, GameObject target, string method, object?[] args)
    {
        var scope = LuaScope.Current;
        using var temps = Temps.Rent(scope.Env);
        return Invoke(scope, temps, mod, InvokeMode.Method, new _JObject(target.Handle), JavaString(scope.Env, method, temps), args);
    }

    public object?[] CallStatic(ModInstance? mod, string table, string function, object?[] args, bool optional = false)
    {
        var scope = LuaScope.Current;
        using var temps = Temps.Rent(scope.Env);
        return Invoke(scope, temps, mod, optional ? InvokeMode.StaticOptional : InvokeMode.Static, JavaString(scope.Env, table, temps),
            JavaString(scope.Env, function, temps), args);
    }

    public object?[] CallStaticMethod(ModInstance? mod, string table, string method, object?[] args)
    {
        var scope = LuaScope.Current;
        using var temps = Temps.Rent(scope.Env);
        return Invoke(scope, temps, mod, InvokeMode.StaticMethod, JavaString(scope.Env, table, temps), JavaString(scope.Env, method, temps), args);
    }

    public object?[] CallGlobal(ModInstance? mod, string name, object?[] args)
    {
        var scope = LuaScope.Current;
        using var temps = Temps.Rent(scope.Env);
        return Invoke(scope, temps, mod, InvokeMode.Global, _JObject.Null, JavaString(scope.Env, name, temps), args);
    }

    public object? GetStatic(string table, string field)
    {
        var scope = LuaScope.Current;
        using var temps = Temps.Rent(scope.Env);
        var jTable = JavaString(scope.Env, table, temps);
        var jField = JavaString(scope.Env, field, temps);
        var result = CallObject(scope.Env, _getStatic, jTable, jField);
        return ValueBuffer.ReadOne(scope, result);
    }

    private static object?[] Invoke(LuaScope scope, Temps temps, ModInstance? mod, InvokeMode mode, _JObject target,
        _JObject name, object?[] args)
    {
        var jValues = stackalloc _JValue[4 + 2 * MaxDirectArgs];
        jValues[0] = new _JValue((int)mode);
        jValues[1] = new _JValue(target);
        jValues[2] = new _JValue(name);
        if (args.Length > MaxDirectArgs)
        {
            jValues[3] = new _JValue(ToJavaArray(scope, args, mod, temps));
            return ValueBuffer.ReadAll(scope, CallObject(scope.Env, _invoke, jValues));
        }
        PackArgs(scope, args, mod, temps, jValues + 3);
        return ValueBuffer.ReadAll(scope, CallObject(scope.Env, InvokeN[args.Length], jValues));
    }

    // Fills the numbers mask then (object, double) per argument; numbers go as the double, so they aren't boxed over JNI.
    private static void PackArgs(LuaScope scope, object?[] args, ModInstance? mod, Temps temps, _JValue* slots)
    {
        if (args.Length == 0)
            return;
        var numbers = 0;
        for (var i = 0; i < args.Length; i++)
        {
            var value = args[i] is System.Runtime.CompilerServices.IUnion union ? union.Value : args[i];
            if (value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal)
            {
                numbers |= 1 << i;
                slots[1 + 2 * i] = new _JValue(_JObject.Null);
                slots[2 + 2 * i] = new _JValue(Convert.ToDouble(value));
            }
            else
            {
                slots[1 + 2 * i] = new _JValue(ToJava(scope, value, mod, temps));
                slots[2 + 2 * i] = new _JValue(0d);
            }
        }
        slots[0] = new _JValue(numbers);
    }


    private static _JObject JavaString(JNIEnv env, string text, Temps temps)
    {
        if (text.Length > MaxCachedLength)
            return temps.String(text);
        if (Strings.TryGetValue(text, out var cached))
            return cached;
        lock (StringsGate)
        {
            if (Strings.TryGetValue(text, out cached))
                return cached;
            if (Strings.Count >= MaxCachedStrings)
                return temps.String(text);
            var local = (_JObject)env.AsJString(text);
            var global = env.NewGlobalRef(local);
            env.DeleteLocalRef(local);
            Strings[text] = global;
            return global;
        }
    }

    public GameObject NewTable()
    {
        var scope = LuaScope.Current;
        return new GameObject(scope, CallObject(scope.Env, _newTable).Value);
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
            case bool flag:
                return flag ? _true : _false;
            case string text:
                return JavaString(env, text, temps);
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
                var table = temps.Add(CallObject(env, _newTable));
                foreach (DictionaryEntry entry in dictionary)
                {
                    var key = ToJava(scope, entry.Key, mod, temps);
                    var item = ToJava(scope, entry.Value, mod, temps);
                    CallVoid(env, _setIndex, table, key, item);
                }
                return table;
            }
            case IEnumerable sequence:
            {
                var table = temps.Add(CallObject(env, _newTable));
                var index = 1;
                foreach (var element in sequence)
                {
                    var key = temps.Add(JavaBoxing.Box(env, (double)index++));
                    var item = ToJava(scope, element, mod, temps);
                    CallVoid(env, _setIndex, table, key, item);
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
        using var temps = Temps.Rent(scope.Env);
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


    // Fixed arities, so a call allocates no argument array. WrenchLua reports Lua errors as LuaError; these surface
    // them to mods as LuaException with just the Lua message.
    private static _JObject CallObject(JNIEnv env, _JMethodID method) => CallObject(env, method, (_JValue*)null);

    private static _JObject CallObject(JNIEnv env, _JMethodID method, _JObject a)
    {
        var args = stackalloc _JValue[] { new _JValue(a) };
        return CallObject(env, method, args);
    }

    private static _JObject CallObject(JNIEnv env, _JMethodID method, _JObject a, _JObject b)
    {
        var args = stackalloc _JValue[] { new _JValue(a), new _JValue(b) };
        return CallObject(env, method, args);
    }

    private static _JObject CallObject(JNIEnv env, _JMethodID method, _JValue* args)
    {
        try
        {
            return env.CallStaticObjectMethod(_wrenchLua, method, args);
        }
        catch (JavaThrowable e)
        {
            throw ToLuaException(e);
        }
    }

    private static void CallVoid(JNIEnv env, _JMethodID method, _JObject a, _JObject b)
    {
        var args = stackalloc _JValue[] { new _JValue(a), new _JValue(b) };
        CallVoid(env, method, args);
    }

    private static void CallVoid(JNIEnv env, _JMethodID method, _JObject a, _JObject b, _JObject c)
    {
        var args = stackalloc _JValue[] { new _JValue(a), new _JValue(b), new _JValue(c) };
        CallVoid(env, method, args);
    }

    private static void CallVoid(JNIEnv env, _JMethodID method, _JValue* args)
    {
        try
        {
            env.CallStaticVoidMethod(_wrenchLua, method, args);
        }
        catch (JavaThrowable e)
        {
            throw ToLuaException(e);
        }
    }

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
    private sealed class KeptScope : IKeptScope
    {
        public string Origin => "a kept copy";

        public bool IsActive { get; private set; } = true;

        public int ThreadId { get; } = Environment.CurrentManagedThreadId;

        public string GetTypeName(nint handle) => JavaClassNames.Of(LuaScope.Current.Env, new _JObject(handle));

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
    internal sealed class Temps : IDisposable
    {
        // Pooled per thread, as every call needs one; nested calls take another off the list.
        [ThreadStatic] private static Temps? _free;
        private JNIEnv _env = null!;
        private List<_JObject>? _refs;
        private Temps? _next;

        public static Temps Rent(JNIEnv env)
        {
            var temps = _free ?? new Temps();
            _free = temps._next;
            temps._env = env;
            return temps;
        }

        public _JObject Add(_JObject obj)
        {
            if (!obj.IsNull)
                (_refs ??= []).Add(obj);
            return obj;
        }

        public _JObject String(string value) => Add((_JObject)_env.AsJString(value));

        /// <summary>Stops tracking <paramref name="obj"/>, for a ref handed back to Java as a native method's result.</summary>
        public _JObject Detach(_JObject obj)
        {
            _refs?.Remove(obj);
            return obj;
        }

        public void Dispose()
        {
            if (_refs is { Count: > 0 })
            {
                foreach (var obj in _refs)
                    _env.DeleteLocalRef(obj);
                _refs.Clear();
            }
            _env = null!;
            _next = _free;
            _free = this;
        }
    }
}
