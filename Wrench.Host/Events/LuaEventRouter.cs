using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using JNIBinding;
using JNIBinding.Bindings.Native.Types;
using Wrench.Host.Logging;
using Wrench.Host.Lua;
using Wrench.Host.Mods;

namespace Wrench.Host.Events;

/// <summary>
/// Routes Lua events from the agent's hook, keeping its subscriptions in sync: host hooks run before the event's Lua
/// callbacks, mod handlers after them, so mods see what vanilla and Lua mods did (e.g. their context-menu options).
/// </summary>
internal static unsafe class LuaEventRouter
{
    private static readonly Logger Log = Logging.Log.For("Wrench.Host");

    private sealed record Registration(ModInstance Mod, Action<LuaEventArgs> Handler);

    private sealed record BatchRegistration(ModInstance Mod, Action<LuaEventBatch> Handler);

    private static readonly ConcurrentDictionary<string, ImmutableArray<Registration>> Handlers = new();
    private static readonly ConcurrentDictionary<string, ImmutableArray<Action>> HostHooks = new();
    private static readonly ConcurrentDictionary<string, ImmutableArray<BatchRegistration>> BatchHandlers = new();
    private static readonly Lock Gate = new();

    // The agent identifies subscribed events by the id subscribe returned, so no name crosses JNI per dispatch.
    private static readonly ConcurrentDictionary<int, string> EventNames = new();
    private static readonly Func<object, string> HandlerOrigin = name => $"the {name} handler";
    private static readonly Func<object, string> HostHookOrigin = name => $"the {name} host hook";
    private static readonly Func<object, string> BatchOrigin = name => $"the {name} batch handler";

    /// <summary>
    /// Runs <paramref name="hook"/> on <paramref name="eventName"/> before its Lua callbacks and mod handlers, so mods
    /// the hook loads receive the same event.
    /// </summary>
    public static void AddHostHook(string eventName, Action hook)
    {
        bool first;
        lock (Gate)
        {
            first = !HostHooks.ContainsKey(eventName);
            HostHooks[eventName] = HostHooks.GetValueOrDefault(eventName, []).Add(hook);
        }
        if (first)
            SetAgentSubscription(eventName, subscribe: true, afterLua: false);
    }

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
            SetAgentSubscription(eventName, subscribe: true, afterLua: true);
        return new Unregistration(() => Unregister(eventName, registration));
    }

    public static IDisposable RegisterBatch(ModInstance mod, string eventName, Action<LuaEventBatch> handler)
    {
        var registration = new BatchRegistration(mod, handler);
        bool first;
        lock (Gate)
        {
            var existing = BatchHandlers.GetValueOrDefault(eventName, []);
            first = existing.IsEmpty;
            BatchHandlers[eventName] = existing.Add(registration);
        }
        if (first)
            SetAgentBatchSubscription(eventName, subscribe: true);
        return new Unregistration(() => UnregisterBatch(eventName, registration));
    }

    private static void UnregisterBatch(string eventName, BatchRegistration registration)
    {
        bool last;
        lock (Gate)
        {
            if (!BatchHandlers.TryGetValue(eventName, out var existing))
                return;
            var remaining = existing.Remove(registration);
            last = remaining.IsEmpty && !existing.IsEmpty;
            if (remaining.IsEmpty)
                BatchHandlers.TryRemove(eventName, out _);
            else
                BatchHandlers[eventName] = remaining;
        }
        if (last)
            SetAgentBatchSubscription(eventName, subscribe: false);
    }

    private static void SetAgentBatchSubscription(string eventName, bool subscribe)
    {
        var env = Entry.CurrentEnv();
        var events = Entry.WrenchEventsClass;
        var name = env.AsJString(eventName);
        _JValue* args = stackalloc _JValue[] { new _JValue((_JObject)name) };
        if (subscribe)
            EventNames[env.CallStaticIntMethod(events, env.GetStaticMethodId(events, "subscribeBatch", "(Ljava/lang/String;)I"), args)] = eventName;
        else
            env.CallStaticVoidMethod(events, env.GetStaticMethodId(events, "unsubscribeBatch", "(Ljava/lang/String;)V"), args);
        env.DeleteLocalRef((_JObject)name);
    }

    [UnmanagedCallersOnly]
    internal static void OnLuaEventBatch(IntPtr env, IntPtr cls, int eventId, IntPtr events)
    {
        var jEnv = JNIEnv.ForHandle(new _JNIEnv(env));
        try
        {
            var eventName = EventNames[eventId];
            if (!BatchHandlers.TryGetValue(eventName, out var registrations) || registrations.IsEmpty)
                return;
            using var scope = LuaScope.Enter(jEnv, mod: null, eventName, BatchOrigin);
            var batch = new LuaEventBatch(eventName, new GameObject(scope, events));
            foreach (var registration in registrations)
            {
                if (!registration.Mod.IsActive)
                    continue;
                scope.Mod = registration.Mod;
                try
                {
                    registration.Handler(batch);
                }
                catch (Exception e)
                {
                    ModHost.Instance.Log(registration.Mod, LogLevel.Error, $"{eventName} batch handler failed", e);
                }
            }
        }
        catch (Exception e)
        {
            Entry.ThrowToJava(jEnv, e);
        }
    }

    /// <summary>
    /// Runs <paramref name="mods"/>' handlers for <paramref name="eventName"/> now, inside the current Lua call. For mods
    /// loaded after the event already fired, such as ones approved from the main menu after OnGameBoot.
    /// </summary>
    public static void InvokeNow(string eventName, IReadOnlyCollection<ModInstance> mods)
    {
        if (!Handlers.TryGetValue(eventName, out var registrations))
            return;
        var scope = LuaScope.Current;
        var previous = scope.Mod;
        var eventArgs = new LuaEventArgs(eventName, []);
        foreach (var registration in registrations.Where(r => r.Mod.IsActive && mods.Contains(r.Mod)))
        {
            scope.Mod = registration.Mod;
            try
            {
                registration.Handler(eventArgs);
            }
            catch (Exception e)
            {
                ModHost.Instance.Log(registration.Mod, LogLevel.Error, $"{eventName} handler failed", e);
            }
        }
        scope.Mod = previous;
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
            SetAgentSubscription(eventName, subscribe: false, afterLua: true);
    }

    private static void SetAgentSubscription(string eventName, bool subscribe, bool afterLua)
    {
        var env = Entry.CurrentEnv();
        var events = Entry.WrenchEventsClass;
        var name = env.AsJString(eventName);
        _JValue* args = stackalloc _JValue[] { new _JValue((_JObject)name), new _JValue(afterLua ? _JBoolean.True : _JBoolean.False) };
        if (subscribe)
            EventNames[env.CallStaticIntMethod(events, env.GetStaticMethodId(events, "subscribe", "(Ljava/lang/String;Z)I"), args)] = eventName;
        else
            env.CallStaticVoidMethod(events, env.GetStaticMethodId(events, "unsubscribe", "(Ljava/lang/String;Z)V"), args);
        env.DeleteLocalRef((_JObject)name);
    }

    [UnmanagedCallersOnly]
    internal static void OnLuaEvent(IntPtr env, IntPtr cls, int eventId, IntPtr args, byte afterLua)
    {
        var jEnv = JNIEnv.ForHandle(new _JNIEnv(env));
        try
        {
            var eventName = EventNames[eventId];
            if (afterLua == 0)
            {
                RunHostHooks(jEnv, eventName);
                return;
            }

            if (!Handlers.TryGetValue(eventName, out var registrations) || registrations.IsEmpty)
                return;
            using var scope = LuaScope.Enter(jEnv, mod: null, eventName, HandlerOrigin);
            var eventArgs = new LuaEventArgs(eventName, ValueBuffer.ReadAll(scope, new _JObject(args)));
            foreach (var registration in registrations)
            {
                if (!registration.Mod.IsActive)
                    continue;
                scope.Mod = registration.Mod;
                try
                {
                    registration.Handler(eventArgs);
                }
                catch (Exception e)
                {
                    ModHost.Instance.Log(registration.Mod, LogLevel.Error, $"{eventName} handler failed", e);
                }
            }
        }
        catch (Exception e)
        {
            Entry.ThrowToJava(jEnv, e);
        }
    }

    private static void RunHostHooks(JNIEnv env, string eventName)
    {
        var hooks = HostHooks.GetValueOrDefault(eventName, []);
        if (hooks.IsEmpty)
            return;
        using var scope = LuaScope.Enter(env, mod: null, eventName, HostHookOrigin);
        foreach (var hook in hooks)
        {
            try
            {
                hook();
            }
            catch (Exception e)
            {
                Log.Error($"Host hook for {eventName} failed", e);
            }
        }
    }

    private sealed class Unregistration(Action unregister) : IDisposable
    {
        private Action? _unregister = unregister;

        public void Dispose() => Interlocked.Exchange(ref _unregister, null)?.Invoke();
    }
}
