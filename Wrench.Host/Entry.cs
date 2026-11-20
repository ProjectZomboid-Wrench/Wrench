using System.Reflection;
using System.Runtime.InteropServices;
using JNIBinding;
using JNIBinding.Bindings;
using JNIBinding.Bindings.Native.Types;
using Wrench.Host.Events;
using Wrench.Host.Game;
using Wrench.Host.Logging;
using Wrench.Host.Lua;
using Wrench.Host.Mods;
using Wrench.Host.Security;

namespace Wrench.Host;

/// <summary>Entry points called from the Java agent (via hostfxr) and from Java native methods.</summary>
public static unsafe class Entry
{
    // Return codes of Attach; keep in sync with io.pznet.wrench.DotnetHost.
    private const int Attached = 0;
    private const int VmNotReady = 1;
    private const int Failed = -1;

    private static readonly Logger Log = Logging.Log.For("Wrench.Host");

    private static JvmLibrary? _library;
    private static JVM? _vm;

    internal static _JClass WrenchEventsClass { get; private set; }

    /// <summary>
    /// Called by the agent until it returns <see cref="Attached"/>. <paramref name="arg"/> is the running JVM's
    /// java.home, UTF-8 encoded. Returns <see cref="VmNotReady"/> while the JVM is still being created.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int Attach(IntPtr arg, int size)
    {
        if (_vm is not null)
            return Attached;
        try
        {
            Logging.Log.CaptureJniBinding();
            _library ??= JvmLibrary.ForJavaHome(Marshal.PtrToStringUTF8(arg, size));
            var vm = JVM.GetCreated(_library);
            if (vm is null)
                return VmNotReady;

            var runtime = new JavaRuntime(vm, vm.GetOrAttachEnv());
            var env = runtime.Env;

            runtime.FindSystemClass("io.pznet.wrench.WrenchNative")
                .BeginRegisterNatives()
                .Add("hello", "(Ljava/lang/String;)V", (delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Hello)
                .Add("onLuaEvent", "(ILjava/lang/Object;Z)V",
                    (delegate* unmanaged<IntPtr, IntPtr, int, IntPtr, byte, void>)&LuaEventRouter.OnLuaEvent)
                .Add("invokeFunction", "(JLjava/lang/Object;)Ljava/lang/Object;",
                    (delegate* unmanaged<IntPtr, IntPtr, long, IntPtr, IntPtr>)&LuaFunctions.Invoke)
                .Add("onPermissionDecision", "(Ljava/lang/String;Z)V",
                    (delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte, void>)&PermissionPrompt.OnPermissionDecision)
                .Complete();
            PermissionPrompt.Initialize(runtime);
            ReportToAgent(runtime);

            LuaBridge.Initialize(runtime);
            JavaClassNames.Initialize(runtime);
            LuaHost.Instance = LuaBridge.Instance;

            var events = runtime.FindSystemClass("io.pznet.wrench.WrenchEvents");
            WrenchEventsClass = env.NewGlobalRef(events.Value).AsClass();

            _vm = vm;
            Log.Info($"Attached to {vm}");

            var mods = ModLoader.LoadAll(ModsDirectory());
            Log.Info($"{mods.Count} Wrench-local mod(s) loaded");
            // First: OnGameBoot follows every Lua reload, and objects from the old state must be let go before mods run.
            LuaEventRouter.AddHostHook(LuaEvents.OnGameBoot, Zomboid.Lua.LuaClass.ReleaseStale);
            LuaEventRouter.AddHostHook(LuaEvents.OnGameBoot, AnnounceToLua);
            LuaEventRouter.AddHostHook(LuaEvents.OnGameBoot, () => PzMods.Sync(LuaBridge.Instance));
            LuaEventRouter.AddHostHook(LuaEvents.OnGameBoot,
                () => MainMenuVersion.Install($"Wrench {Version} ({RuntimeInformation.FrameworkDescription})"));
            return Attached;
        }
        catch (Exception e)
        {
            Log.Error("Attach failed", e);
            return Failed;
        }
    }

    // Lets Lua (notably the Wrench PZ metamod) detect the loader. Host hooks run before Lua's own OnGameBoot callbacks.
    private static void AnnounceToLua() =>
        LuaBridge.Instance.SetHostGlobal("WrenchHost", new Dictionary<string, object?> { ["version"] = Version });

    // Lets the agent's splash say which host it's running.
    private static void ReportToAgent(JavaRuntime runtime)
    {
        var env = runtime.Env;
        var info = runtime.FindSystemClass("io.pznet.wrench.WrenchInfo").Value;
        var setHost = env.GetStaticMethodId(info, "setHost", "(Ljava/lang/String;Ljava/lang/String;)V");
        var version = env.AsJString(Version);
        var dotnet = env.AsJString(RuntimeInformation.FrameworkDescription);
        _JValue* args = stackalloc _JValue[] { new _JValue((_JObject)version), new _JValue((_JObject)dotnet) };
        env.CallStaticVoidMethod(info, setHost, args);
        env.DeleteLocalRef((_JObject)version);
        env.DeleteLocalRef((_JObject)dotnet);
    }

    private static string Version =>
        typeof(Entry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "unknown";

    /// <summary>The calling thread's JNIEnv, attaching .NET-owned threads as daemons on first use.</summary>
    internal static JNIEnv CurrentEnv() =>
        (_vm ?? throw new InvalidOperationException("Wrench.Host isn't attached to a JVM")).GetOrAttachEnv(asDaemon: true);

    // Beside host/ in the shipped layout: wrench/{wrench-agent.jar, host/, mods/, runtime/}.
    private static string ModsDirectory() =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Entry).Assembly.Location)!, "..", "mods"));

    [UnmanagedCallersOnly]
    private static void Hello(IntPtr env, IntPtr cls, IntPtr message)
    {
        var jEnv = JNIEnv.ForHandle(new _JNIEnv(env));
        try
        {
            Log.Info($"hello {jEnv.AsString(new _JString(message))}");
        }
        catch (Exception e)
        {
            ThrowToJava(jEnv, e);
        }
    }

    // Exceptions must not unwind through native frames; re-raise them on the Java side instead.
    internal static void ThrowToJava(JNIEnv env, Exception e) => ThrowToJava(env, $"[Wrench.Host] {e}");

    internal static void ThrowToJava(JNIEnv env, string text)
    {
        var cls = env.FindClass("java/lang/RuntimeException");
        var message = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            env.ThrowNew(cls, (byte*)message);
        }
        finally
        {
            Marshal.FreeCoTaskMem(message);
        }
    }
}
