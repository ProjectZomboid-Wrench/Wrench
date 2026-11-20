using JNIBinding;
using JNIBinding.Bindings.Native.Types;

namespace Wrench.Host.Lua;

/// <summary>
/// Java class names by the agent's <c>WrenchClasses</c> ids: one int call per lookup, and the name itself only
/// crosses JNI the first time a class is seen.
/// </summary>
internal static unsafe class JavaClassNames
{
    private static readonly Lock Gate = new();
    private static _JClass _classes;
    private static _JMethodID _id, _name;
    private static string?[] _names = new string?[256];

    public static void Initialize(JavaRuntime runtime)
    {
        var env = runtime.Env;
        _classes = env.NewGlobalRef(runtime.FindSystemClass("io.pznet.wrench.WrenchClasses").Value).AsClass();
        _id = env.GetStaticMethodId(_classes, "id", "(Ljava/lang/Object;)I");
        _name = env.GetStaticMethodId(_classes, "name", "(I)Ljava/lang/String;");
    }

    public static string Of(JNIEnv env, _JObject obj)
    {
        _JValue* args = stackalloc _JValue[] { new _JValue(obj) };
        var id = env.CallStaticIntMethod(_classes, _id, args);
        var names = _names;
        return id < names.Length && names[id] is { } name ? name : Fetch(env, id);
    }

    private static string Fetch(JNIEnv env, int id)
    {
        _JValue* args = stackalloc _JValue[] { new _JValue(id) };
        var value = env.CallStaticObjectMethod(_classes, _name, args);
        var name = env.AsString(value.AsString());
        env.DeleteLocalRef(value);
        lock (Gate)
        {
            // Copy-on-write, so readers on the fast path never see a half-filled array.
            var names = _names;
            var size = names.Length;
            while (size <= id)
                size *= 2;
            var copy = new string?[size];
            names.CopyTo(copy, 0);
            copy[id] = name;
            _names = copy;
        }
        return name;
    }
}
