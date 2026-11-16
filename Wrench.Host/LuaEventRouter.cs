using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using JNIBinding;
using JNIBinding.Bindings.Native.Types;

namespace Wrench.Host;

/// <summary>Routes Lua events from the agent's hook to mod handlers, keeping the agent's subscription set in sync.</summary>
internal static unsafe class LuaEventRouter
{
    private sealed record Registration(ModInstance Mod, Action<LuaEventArgs> Handler);

    private static readonly ConcurrentDictionary<string, ImmutableArray<Registration>> Handlers = new();
    private static readonly Lock Gate = new();

    public static IDisposable Register(ModInstance mod, string eventName, Action<LuaEventArgs> handler)
    {
        var registration = new Registration(mod, handler);
        bool first;
        lock (Gate)
        {
            var existing = Handlers.GetValueOrDefault(eventName, []);
            first = existing.IsEmpty;
            Handlers[eventName] = existing.Add(registration);
        }
        if (first)
            SetAgentSubscription(eventName, subscribe: true);
        return new Unregistration(() => Unregister(eventName, registration));
    }

    private static void Unregister(string eventName, Registration registration)
    {
        bool last;
        lock (Gate)
        {
            if (!Handlers.TryGetValue(eventName, out var existing))
                return;
            var remaining = existing.Remove(registration);
            last = remaining.IsEmpty && !existing.IsEmpty;
            if (remaining.IsEmpty)
                Handlers.TryRemove(eventName, out _);
            else
                Handlers[eventName] = remaining;
        }
        if (last)
            SetAgentSubscription(eventName, subscribe: false);
    }

    private static void SetAgentSubscription(string eventName, bool subscribe)
    {
        var env = Entry.CurrentEnv();
        var events = Entry.WrenchEventsClass;
        var method = env.GetStaticMethodId(events, subscribe ? "subscribe" : "unsubscribe", "(Ljava/lang/String;)V");
        var name = env.AsJString(eventName);
        _JValue* args = stackalloc _JValue[] { new _JValue((_JObject)name) };
        env.CallStaticVoidMethod(events, method, args);
        env.DeleteLocalRef((_JObject)name);
    }

    [UnmanagedCallersOnly]
    internal static void OnLuaEvent(IntPtr env, IntPtr cls, IntPtr name, IntPtr args)
    {
        var jEnv = JNIEnv.ForHandle(new _JNIEnv(env));
        try
        {
            var eventName = jEnv.AsString(new _JString(name));
            if (!Handlers.TryGetValue(eventName, out var registrations) || registrations.IsEmpty)
                return;

            using var scope = new DispatchScope(jEnv);
            var eventArgs = new LuaEventArgs(eventName, JavaValues.ToDotnet(jEnv, new _JObjectArray(args), scope));
            foreach (var registration in registrations)
            {
                try
                {
                    registration.Handler(eventArgs);
                }
                catch (Exception e)
                {
                    ModHost.Instance.Log(registration.Mod, ModLogLevel.Error, $"{eventName} handler failed", e);
                }
            }
        }
        catch (Exception e)
        {
            Entry.ThrowToJava(jEnv, e);
        }
    }

    private sealed class Unregistration(Action unregister) : IDisposable
    {
        private Action? _unregister = unregister;

        public void Dispose() => Interlocked.Exchange(ref _unregister, null)?.Invoke();
    }

    /// <summary>Lifetime of the game objects handed to one dispatch; local refs die when the native call returns.</summary>
    private sealed class DispatchScope(JNIEnv env) : IGameObjectScope, IDisposable
    {
        public bool IsActive { get; private set; } = true;

        public int ThreadId { get; } = Environment.CurrentManagedThreadId;

        public string GetTypeName(nint handle) => JavaBoxing.GetClassName(env, new _JObject(handle));

        public string Describe(nint handle) => JavaBoxing.ToJavaString(env, new _JObject(handle));

        public void Dispose() => IsActive = false;
    }
}
