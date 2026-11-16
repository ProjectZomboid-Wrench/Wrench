using System.Runtime.InteropServices;
using JNIBinding;
using JNIBinding.Bindings;
using JNIBinding.Bindings.Native.Types;

namespace Wrench.Host;

/// <summary>Entry points called from the Java agent (via hostfxr) and from Java native methods.</summary>
public static unsafe class Entry
{
    // Return codes of Attach; keep in sync with io.pznet.wrench.DotnetHost.
    private const int Attached = 0;
    private const int VmNotReady = 1;
    private const int Failed = -1;

    private static JvmLibrary? _library;
    private static JVM? _vm;

    [ThreadStatic] private static JNIEnv? _threadEnv;

    private static readonly string[] DiagnosticEvents = ["OnGameBoot", "OnMainMenuEnter"];

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
            _library ??= JvmLibrary.ForJavaHome(Marshal.PtrToStringUTF8(arg, size));
            var vm = JVM.GetCreated(_library);
            if (vm is null)
                return VmNotReady;

            var runtime = new JavaRuntime(vm, vm.GetOrAttachEnv());
            runtime.FindSystemClass("io.pznet.wrench.WrenchNative")
                .BeginRegisterNatives()
                .Add("hello", "(Ljava/lang/String;)V", (delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Hello)
                .Add("onLuaEvent", "(Ljava/lang/String;[Ljava/lang/Object;)V",
                    (delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&LuaEvents.OnLuaEvent)
                .Complete();

            foreach (var name in DiagnosticEvents)
                LuaEvents.Subscribe(runtime, name, LogEvent);

            _vm = vm;
            Console.WriteLine($"[Wrench.Host] Attached to {vm}");
            return Attached;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Wrench.Host] Attach failed: {e}");
            return Failed;
        }
    }

    [UnmanagedCallersOnly]
    private static void Hello(IntPtr env, IntPtr cls, IntPtr message)
    {
        var jEnv = EnvFor(env);
        try
        {
            Console.WriteLine($"[Wrench.Host] hello {jEnv.AsString(new _JString(message))}");
        }
        catch (Exception e)
        {
            ThrowToJava(jEnv, e);
        }
    }

    private static void LogEvent(JNIEnv env, string name, _JObjectArray args) =>
        Console.WriteLine($"[Wrench.Host] Lua event {name}({string.Join(", ", LuaEvents.Describe(env, args))})");

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

    // Wrapping a JNIEnv binds every JNI function, so reuse it; the pointer is fixed per thread.
    internal static JNIEnv EnvFor(IntPtr env)
    {
        if (_threadEnv is null || _threadEnv.EnvHandle.Value != env)
            _threadEnv = new JNIEnv(new _JNIEnv(env));
        return _threadEnv;
    }
}
