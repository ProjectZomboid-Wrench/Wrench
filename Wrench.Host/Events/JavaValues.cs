using JNIBinding;
using JNIBinding.Bindings.Native.Types;

namespace Wrench.Host.Events;

/// <summary>Turns Lua event arguments into what mods see: plain .NET values, or <see cref="GameObject"/>s.</summary>
internal static class JavaValues
{
    public static object?[] ToDotnet(JNIEnv env, _JObjectArray array, IGameObjectScope scope)
    {
        if (array.IsNull)
            return [];
        var values = new object?[env.GetArrayLength(new _JArray(array))];
        for (var i = 0; i < values.Length; i++)
        {
            var element = env.GetObjectArrayElement(array, i);
            if (JavaBoxing.TryUnbox(env, element, out var value))
            {
                values[i] = value;
                if (!element.IsNull)
                    env.DeleteLocalRef(element);
            }
            else
            {
                // The local ref stays alive until the native call returns, which bounds the GameObject's lifetime.
                values[i] = new GameObject(scope, element.Value);
            }
        }
        return values;
    }
}
