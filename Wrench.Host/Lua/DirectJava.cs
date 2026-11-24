using JNIBinding;
using JNIBinding.Bindings.Native.Types;
using JNIBinding.Exceptions;

namespace Wrench.Host.Lua;

/// <summary>
/// Calls Java methods on game objects straight through JNI, bypassing Kahlua: the pilot for generated wrappers that
/// know each method's exact signature. Only exposed methods may be called this way, as Kahlua would only reach those.
/// </summary>
internal static unsafe class DirectJava
{
    /// <summary>The method's id, from the class's system loader; resolve once and keep it.</summary>
    public static _JMethodID Method(string className, string name, string signature)
    {
        var env = LuaScope.Current.Env;
        var runtime = new JavaRuntime(Entry.Vm, env);
        return env.GetMethodId(runtime.FindSystemClass(className).Value, name, signature);
    }

    public static float CallFloat(GameObject target, _JMethodID method)
    {
        var env = LuaScope.Current.Env;
        try
        {
            return env.CallFloatMethod(new _JObject(target.Handle), method, null);
        }
        catch (JavaThrowable e)
        {
            throw new LuaException(e.Message);
        }
    }

    /// <summary>A method taking one float and returning an object, as a game object of the current scope (or null).</summary>
    public static GameObject? CallObject(GameObject target, _JMethodID method, float arg)
    {
        var scope = LuaScope.Current;
        _JValue* args = stackalloc _JValue[] { new _JValue(arg) };
        _JObject result;
        try
        {
            result = scope.Env.CallObjectMethod(new _JObject(target.Handle), method, args);
        }
        catch (JavaThrowable e)
        {
            throw new LuaException(e.Message);
        }
        return result.IsNull ? null : new GameObject(scope, result.Value);
    }
}
