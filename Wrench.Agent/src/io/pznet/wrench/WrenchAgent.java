package io.pznet.wrench;

import java.lang.instrument.ClassFileTransformer;
import java.lang.instrument.Instrumentation;
import java.security.ProtectionDomain;
import java.util.concurrent.atomic.AtomicInteger;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/**
 * Agent arguments (comma-separated key=value): {@code host=<dir>} for Wrench.Host's output
 * (default {@code <agent dir>/host}) and {@code dotnet=<dir>} for the .NET root to load hostfxr from.
 */
public final class WrenchAgent {
    private WrenchAgent() {
    }

    public static void premain(String agentArgs, Instrumentation instrumentation) throws Throwable {
        var args = parseArgs(agentArgs);
        var agentDir = Path.of(WrenchAgent.class.getProtectionDomain().getCodeSource().getLocation().toURI()).getParent();

        var hostDir = args.containsKey("host") ? Path.of(args.get("host")) : agentDir.resolve("host");
        var dotnetRoot = args.containsKey("dotnet") ? Path.of(args.get("dotnet")) : DotnetRoot.locate(agentDir);

        var host = DotnetHost.load(dotnetRoot, hostDir);
        // Advice is inlined, so ByteBuddy never needs its sun.misc.Unsafe class injector (which JDK 24+ warns about).
        System.setProperty("net.bytebuddy.safe", "true");
        LuaEventHook.install(instrumentation);
        PermissionPromptHook.install(instrumentation);
        // Must stay last: the transformer only fires for classes loaded after it's added.
        instrumentation.addTransformer(new AttachWhenVmReady(host, instrumentation));
    }

    /**
     * JNI_GetCreatedJavaVMs doesn't report the VM until creation completes, which is after premain
     * returns, so Wrench.Host attaches on the first application class load instead.
     */
    private static final class AttachWhenVmReady implements ClassFileTransformer {
        private static final int IDLE = 0, ATTACHING = 1, DONE = 2;

        private final DotnetHost host;
        private final Instrumentation instrumentation;
        private final String javaHome = System.getProperty("java.home");
        private final AtomicInteger state = new AtomicInteger(IDLE);

        AttachWhenVmReady(DotnetHost host, Instrumentation instrumentation) {
            this.host = host;
            this.instrumentation = instrumentation;
        }

        @Override
        public byte[] transform(ClassLoader loader, String className, Class<?> redefined,
                                ProtectionDomain domain, byte[] classfile) {
            // Attaching loads classes itself, which re-enters here; the state check makes that a no-op.
            if (loader == null || !state.compareAndSet(IDLE, ATTACHING))
                return null;
            try {
                var rc = host.attach(javaHome);
                if (rc == DotnetHost.VM_NOT_READY) {
                    state.set(IDLE);
                    return null;
                }
                state.set(DONE);
                instrumentation.removeTransformer(this);
                if (rc != DotnetHost.ATTACHED)
                    throw new IllegalStateException("Wrench.Host attach failed: " + rc);
                WrenchNative.hello("from Java");
            } catch (Throwable t) {
                // Instrumentation silently swallows exceptions thrown by transformers.
                System.err.println("[Wrench] " + t);
                t.printStackTrace();
            }
            return null;
        }
    }

    private static Map<String, String> parseArgs(String agentArgs) {
        var result = new HashMap<String, String>();
        if (agentArgs == null || agentArgs.isBlank())
            return result;
        for (var pair : agentArgs.split(",")) {
            var idx = pair.indexOf('=');
            if (idx > 0)
                result.put(pair.substring(0, idx).trim(), pair.substring(idx + 1).trim());
        }
        return result;
    }

    static final class DotnetRoot {
        private DotnetRoot() {
        }

        /** Bundled runtime first, then DOTNET_ROOT, dotnet on PATH, and the platform's default install location. */
        static Path locate(Path agentDir) throws Exception {
            var candidates = new java.util.ArrayList<Path>();
            candidates.add(agentDir.resolve("runtime"));
            var env = System.getenv("DOTNET_ROOT");
            if (env != null && !env.isBlank())
                candidates.add(Path.of(env));
            var onPath = fromPath();
            if (onPath != null)
                candidates.add(onPath);
            if (DotnetHost.WINDOWS)
                candidates.add(Path.of(System.getenv().getOrDefault("ProgramFiles", "C:\\Program Files"), "dotnet"));
            else if (DotnetHost.MAC)
                candidates.add(Path.of("/usr/local/share/dotnet"));
            else
                for (var p : new String[] {"/usr/share/dotnet", "/usr/lib/dotnet", "/usr/lib64/dotnet"})
                    candidates.add(Path.of(p));

            for (var candidate : candidates)
                if (Files.isDirectory(candidate.resolve("host").resolve("fxr")))
                    return candidate;
            throw new IllegalStateException("No .NET runtime found. Tried: " + candidates);
        }

        private static Path fromPath() throws Exception {
            var exe = DotnetHost.WINDOWS ? "dotnet.exe" : "dotnet";
            var path = System.getenv("PATH");
            if (path == null)
                return null;
            for (var dir : path.split(java.io.File.pathSeparator)) {
                if (dir.isBlank())
                    continue;
                var candidate = Path.of(dir, exe);
                if (Files.isRegularFile(candidate))
                    return candidate.toRealPath().getParent();
            }
            return null;
        }
    }
}
