using JNIBinding;
using JNIBinding.Bindings.Native.Types;
using Wrench.Host.Lua;

namespace Wrench.Benchmarks.Harness;

/// <summary>
/// Stands in for one native entry from Java: a local frame that frees the refs a batch of calls creates, and the
/// Lua scope those calls need.
/// </summary>
internal readonly ref struct JavaBatch
{
    private readonly JNIEnv _env;
    private readonly LuaScope _scope;

    public JavaBatch(JNIEnv env, ModInstance? mod = null, int capacity = 4 * BenchConfig.Ops)
    {
        _env = env;
        env.PushLocalFrame(capacity);
        _scope = LuaScope.Enter(env, mod, "a benchmark");
    }

    public void Dispose()
    {
        _scope.Dispose();
        _env.PopLocalFrame(_JObject.Null);
    }
}
