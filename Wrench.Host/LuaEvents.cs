using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using JNIBinding;
using JNIBinding.Bindings.Native.Types;

namespace Wrench.Host;

/// <summary>Lua events forwarded by the agent's hook on <c>zombie.Lua.Event.trigger</c>.</summary>
public static unsafe class LuaEvents
{
    /// <param name="args">The event's Lua arguments as a Java <c>Object[]</c>; may be null. Valid only during the call.</param>
    public delegate void Handler(JNIEnv env, string name, _JObjectArray args);

    private static readonly ConcurrentDictionary<string, Handler> Handlers = new();

    /// <summary>Adds a handler and tells the agent to start forwarding <paramref name="name"/>.</summary>
    public static void Subscribe(JavaRuntime runtime, string name, Handler handler)
    {
        Handlers.AddOrUpdate(name, handler, (_, existing) => existing + handler);

        var env = runtime.Env;
        var events = runtime.FindSystemClass("io.pznet.wrench.WrenchEvents");
        var subscribe = env.GetStaticMethodId(events, "subscribe", "(Ljava/lang/String;)V");
        var jName = env.AsJString(name);
        _JValue* args = stackalloc _JValue[] { new _JValue((_JObject)jName) };
        env.CallStaticVoidMethod(events, subscribe, args);
        env.DeleteLocalRef((_JObject)jName);
    }

    /// <summary>Renders each argument with Java's <c>String.valueOf</c>; for diagnostics, not hot paths.</summary>
    public static string[] Describe(JNIEnv env, _JObjectArray args)
    {
        if (args.IsNull)
            return [];

        var stringCls = env.FindClass("java/lang/String");
        var valueOf = env.GetStaticMethodId(stringCls, "valueOf", "(Ljava/lang/Object;)Ljava/lang/String;");
        var result = new string[env.GetArrayLength(new _JArray(args))];
        _JValue* callArgs = stackalloc _JValue[1];
        for (var i = 0; i < result.Length; i++)
        {
            var element = env.GetObjectArrayElement(args, i);
            callArgs[0] = new _JValue(element);
            var text = env.CallStaticObjectMethod(stringCls, valueOf, callArgs);
            result[i] = env.AsString(text.AsString());
            env.DeleteLocalRef(text);
            env.DeleteLocalRef(element);
        }
        return result;
    }

    [UnmanagedCallersOnly]
    internal static void OnLuaEvent(IntPtr env, IntPtr cls, IntPtr name, IntPtr args)
    {
        var jEnv = Entry.EnvFor(env);
        try
        {
            var eventName = jEnv.AsString(new _JString(name));
            if (Handlers.TryGetValue(eventName, out var handler))
                handler(jEnv, eventName, new _JObjectArray(args));
        }
        catch (Exception e)
        {
            Entry.ThrowToJava(jEnv, e);
        }
    }
}
