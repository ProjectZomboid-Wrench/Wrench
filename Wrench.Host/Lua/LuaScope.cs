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

    private LuaScope(JNIEnv env, ModInstance? mod, LuaScope? outer)
    {
        Env = env;
        Mod = mod;
        _outer = outer;
    }

    /// <summary>The innermost active scope on this thread.</summary>
    public static LuaScope Current => _current ?? throw new InvalidOperationException(
        "Lua can only be used on the game thread, inside a Lua event handler or a function called from Lua.");

    public JNIEnv Env { get; }

    /// <summary>The mod whose code is running; owns delegates converted to Lua functions.</summary>
    public ModInstance? Mod { get; set; }

    public bool IsActive { get; private set; } = true;

    public int ThreadId { get; } = Environment.CurrentManagedThreadId;

    public static LuaScope Enter(JNIEnv env, ModInstance? mod) => _current = new LuaScope(env, mod, _current);

    public string GetTypeName(nint handle) => JavaBoxing.GetClassName(Env, new _JObject(handle));

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
