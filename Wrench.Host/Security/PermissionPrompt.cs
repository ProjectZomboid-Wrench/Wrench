using System.Runtime.InteropServices;
using JNIBinding;
using JNIBinding.Bindings.Native.Types;
using Wrench.Host.Lua;
using Wrench.Host.Mods;

namespace Wrench.Host.Security;

/// <summary>
/// The agent's permission prompt (io.pznet.wrench.WrenchPermissions / WrenchPromptState): a game state Lua can't see
/// into or answer. Pending mods are queued over JNI; the player's answer arrives through
/// <see cref="OnPermissionDecision"/>. Nothing here is reachable from Lua.
/// </summary>
internal static unsafe class PermissionPrompt
{
    private static _JClass _permissions, _string;
    private static _JMethodID _clear, _request;

    public static void Initialize(JavaRuntime runtime)
    {
        var env = runtime.Env;
        _permissions = env.NewGlobalRef(runtime.FindSystemClass("io.pznet.wrench.WrenchPermissions").Value).AsClass();
        var stringClass = env.FindClass("java/lang/String");
        _string = env.NewGlobalRef(stringClass).AsClass();
        env.DeleteLocalRef(stringClass);
        _clear = env.GetStaticMethodId(_permissions, "clear", "()V");
        _request = env.GetStaticMethodId(_permissions, "request", "(Ljava/lang/String;Ljava/lang/String;[Ljava/lang/String;)V");
    }

    public static void Clear() => Entry.CurrentEnv().CallStaticVoidMethod(_permissions, _clear, null);

    public static void Request(string id, string name, IReadOnlyList<string> descriptions)
    {
        var env = Entry.CurrentEnv();
        var jId = env.AsJString(id);
        var jName = env.AsJString(name);
        var array = env.NewObjectArray(descriptions.Count, _string, _JObject.Null);
        for (var i = 0; i < descriptions.Count; i++)
        {
            var description = env.AsJString(descriptions[i]);
            env.SetObjectArrayElement(array, i, description);
            env.DeleteLocalRef((_JObject)description);
        }
        _JValue* args = stackalloc _JValue[] { new _JValue((_JObject)jId), new _JValue((_JObject)jName), new _JValue(new _JObject(array.Value)) };
        env.CallStaticVoidMethod(_permissions, _request, args);
        env.DeleteLocalRef((_JObject)jId);
        env.DeleteLocalRef((_JObject)jName);
        env.DeleteLocalRef(new _JObject(array.Value));
    }

    /// <summary>WrenchNative.onPermissionDecision, called by the prompt state on the game thread.</summary>
    [UnmanagedCallersOnly]
    internal static void OnPermissionDecision(IntPtr env, IntPtr cls, IntPtr modId, byte allow)
    {
        var jEnv = JNIEnv.ForHandle(new _JNIEnv(env));
        try
        {
            using var scope = LuaScope.Enter(jEnv, mod: null, "the permission decision");
            PzMods.Decide(jEnv.AsString(new _JString(modId)), allow != 0);
        }
        catch (Exception e)
        {
            Entry.ThrowToJava(jEnv, e);
        }
    }
}
