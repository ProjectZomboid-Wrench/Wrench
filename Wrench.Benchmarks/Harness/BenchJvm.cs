using System.Text;
using JNIBinding;
using JNIBinding.Bindings;
using JNIBinding.Bindings.Native.Types;
using Wrench.Host;

namespace Wrench.Benchmarks.Harness;

/// <summary>
/// The process's one JVM, created in-process with the agent jar and the game's real Kahlua on its class path, with
/// Wrench.Host attached through <see cref="Entry.Attach"/> the way the agent does it.
/// </summary>
internal static unsafe class BenchJvm
{
    private static readonly Lock Gate = new();
    private static JVM? _vm;

    public static _JClass Fixtures { get; private set; }

    /// <summary>The calling thread's JNIEnv, creating the JVM on first use and making this Kahlua's owner thread.</summary>
    public static JNIEnv Start()
    {
        lock (Gate)
        {
            if (_vm is null)
                Create();
        }
        var env = _vm!.GetOrAttachEnv();
        env.CallStaticVoidMethod(Fixtures, env.GetStaticMethodId(Fixtures, "claimThread", "()V"), null);
        return env;
    }

    private static void Create()
    {
        var java = Path.Combine(AppContext.BaseDirectory, "java");
        var pzJar = Path.Combine(PzDirectory(), "projectzomboid.jar");
        if (!File.Exists(pzJar))
            throw new FileNotFoundException($"projectzomboid.jar not found at {pzJar}; set PZ_DIR to the game's projectzomboid/ folder.");
        var classPath = string.Join(Path.PathSeparator,
            Path.Combine(java, "classes"), Path.Combine(java, "wrench-agent.jar"), pzJar);

        var library = new JvmLibrary();
        library.LoadLibrary();
        _vm = JVM.CreateOrGetCreated(library, [$"-Djava.class.path={classPath}", "-Djava.awt.headless=true"]);
        var env = _vm.GetOrAttachEnv();

        var fixtures = env.FindClass("io/pznet/wrench/bench/BenchFixtures");
        Fixtures = env.NewGlobalRef(fixtures).AsClass();
        env.DeleteLocalRef(fixtures);
        env.CallStaticVoidMethod(Fixtures, env.GetStaticMethodId(Fixtures, "setup", "()V"), null);

        var home = Encoding.UTF8.GetBytes(JavaHome(env));
        int result;
        fixed (byte* text = home)
            result = ((delegate* unmanaged<IntPtr, int, int>)&Entry.Attach)((IntPtr)text, home.Length);
        if (result != 0)
            throw new InvalidOperationException($"Entry.Attach returned {result}");
    }

    private static string JavaHome(JNIEnv env)
    {
        var system = env.FindClass("java/lang/System");
        var getProperty = env.GetStaticMethodId(system, "getProperty", "(Ljava/lang/String;)Ljava/lang/String;");
        var key = env.AsJString("java.home");
        _JValue* args = stackalloc _JValue[] { new _JValue((_JObject)key) };
        var value = env.CallStaticObjectMethod(system, getProperty, args);
        var home = env.AsString(value.AsString());
        env.DeleteLocalRef(value);
        env.DeleteLocalRef((_JObject)key);
        env.DeleteLocalRef(system);
        return home;
    }

    /// <summary>A new <c>zombie.core.Color</c> as a global ref, for benchmarks below the Wrench API.</summary>
    public static _JObject NewColor(JNIEnv env)
    {
        var local = env.CallStaticObjectMethod(Fixtures, env.GetStaticMethodId(Fixtures, "color", "()Ljava/lang/Object;"), null);
        var global = env.NewGlobalRef(local);
        env.DeleteLocalRef(local);
        return global;
    }

    private static string PzDirectory() =>
        Environment.GetEnvironmentVariable("PZ_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local/share/Steam/steamapps/common/ProjectZomboid/projectzomboid");
}
