using JNIBinding;
using JNIBinding.Bindings.Native.Types;

namespace Wrench.Host.Lua;

/// <summary>
/// One native entry from Java (an event dispatch or a Lua call into .NET). Game objects created during it wrap JNI
/// local refs, which die when the native call returns, so they're only valid while the scope is active.
/// </summary>
internal sealed class LuaScope : IGameObjectScope, IDisposable
{
    [ThreadStatic] private static LuaScope? _current;
    private readonly LuaScope? _outer;

    private readonly Func<object, string>? _describe;
    private readonly object _origin;

    private LuaScope(JNIEnv env, ModInstance? mod, object origin, Func<object, string>? describe, LuaScope? outer)
    {
        Env = env;
        Mod = mod;
        _origin = origin;
        _describe = describe;
        _outer = outer;
    }

    /// <summary>The innermost active scope on this thread.</summary>
    public static LuaScope Current => _current ?? throw new InvalidOperationException(
        "Lua can only be used on the game thread, inside a Lua event handler or a function called from Lua.");

    public JNIEnv Env { get; }

    /// <summary>The mod whose code is running; owns delegates converted to Lua functions.</summary>
    public ModInstance? Mod { get; set; }

    /// <summary>Where this scope's game objects came from, for error messages; built only when asked for.</summary>
    public string Origin => _describe is null ? (string)_origin : _describe(_origin);

    public bool IsActive { get; private set; } = true;

    public int ThreadId { get; } = Environment.CurrentManagedThreadId;

    public static LuaScope Enter(JNIEnv env, ModInstance? mod, string origin) =>
        _current = new LuaScope(env, mod, origin, null, _current);

    /// <summary>Enters a scope whose origin is <paramref name="describe"/>(<paramref name="subject"/>), so hot entries don't format one per call.</summary>
    public static LuaScope Enter(JNIEnv env, ModInstance? mod, object subject, Func<object, string> describe) =>
        _current = new LuaScope(env, mod, subject, describe, _current);

    public string GetTypeName(nint handle) => JavaClassNames.Of(Env, new _JObject(handle));

    public string Describe(nint handle) => JavaBoxing.ToJavaString(Env, new _JObject(handle));

    public void Release(nint handle)
    {
    }

    public void Dispose()
    {
        IsActive = false;
        _current = _outer;
    }
}
