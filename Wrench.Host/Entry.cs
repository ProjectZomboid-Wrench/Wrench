using System.Runtime.InteropServices;
using JNIBinding;
using JNIBinding.Bindings;
using JNIBinding.Bindings.Native.Types;
using Wrench.Host.Events;
using Wrench.Host.Logging;
using Wrench.Host.Mods;

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
                .Add("onLuaEvent", "(Ljava/lang/String;[Ljava/lang/Object;)V",
                    (delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&LuaEventRouter.OnLuaEvent)
                .Complete();

            var events = runtime.FindSystemClass("io.pznet.wrench.WrenchEvents");
            WrenchEventsClass = env.NewGlobalRef(events.Value).AsClass();

            _vm = vm;
            Log.Info($"Attached to {vm}");

            var mods = ModLoader.LoadAll(ModsDirectory());
            Log.Info($"{mods.Count} mod(s) loaded");
            return Attached;
        }
        catch (Exception e)
        {
            Log.Error("Attach failed", e);
            return Failed;
        }
    }

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
    internal static void ThrowToJava(JNIEnv env, Exception e)
    {
        var cls = env.FindClass("java/lang/RuntimeException");
        var message = Marshal.StringToCoTaskMemUTF8($"[Wrench.Host] {e}");
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
