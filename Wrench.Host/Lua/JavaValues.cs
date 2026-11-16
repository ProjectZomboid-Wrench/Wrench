using JNIBinding;
using JNIBinding.Bindings.Native.Types;

namespace Wrench.Host.Lua;

/// <summary>Turns Java values into what mods see: plain .NET values, or <see cref="GameObject"/>s.</summary>
internal static class JavaValues
{
    public static object?[] ToDotnet(JNIEnv env, _JObjectArray array, IGameObjectScope scope)
    {
        if (array.IsNull)
            return [];
        var values = new object?[env.GetArrayLength(new _JArray(array))];
        for (var i = 0; i < values.Length; i++)
            values[i] = ToDotnet(env, env.GetObjectArrayElement(array, i), scope);
        return values;
    }

    /// <summary>Takes ownership of <paramref name="obj"/>: unboxed values release it, game objects keep it for the scope.</summary>
    public static object? ToDotnet(JNIEnv env, _JObject obj, IGameObjectScope scope)
    {
        if (!JavaBoxing.TryUnbox(env, obj, out var value))
            return new GameObject(scope, obj.Value);
        if (!obj.IsNull)
            env.DeleteLocalRef(obj);
        return value;
    }
}
