package io.pznet.wrench;

import java.lang.foreign.Arena;
import java.lang.foreign.FunctionDescriptor;
import java.lang.foreign.Linker;
import java.lang.foreign.MemoryLayout;
import java.lang.foreign.MemorySegment;
import java.lang.foreign.StructLayout;
import java.lang.foreign.SymbolLookup;
import java.lang.foreign.ValueLayout;
import java.lang.invoke.MethodHandle;
import java.nio.charset.Charset;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.Comparator;

/** Starts the CLR in-process through hostfxr and resolves Wrench.Host's {@code Entry.Attach}. */
final class DotnetHost {
    // Return codes of Entry.Attach; keep in sync with Wrench.Host.Entry.
    static final int ATTACHED = 0;
    static final int VM_NOT_READY = 1;

    static final boolean WINDOWS = System.getProperty("os.name").startsWith("Windows");
    static final boolean MAC = System.getProperty("os.name").startsWith("Mac");

    private static final String HOST_ASSEMBLY = "Wrench.Host";
    private static final String ENTRY_TYPE = "Wrench.Host.Entry, Wrench.Host";
    private static final String ENTRY_METHOD = "Attach";

    private static final int HDT_LOAD_ASSEMBLY_AND_GET_FUNCTION_POINTER = 5;
    private static final MemorySegment UNMANAGEDCALLERSONLY_METHOD = MemorySegment.ofAddress(-1L);

    // hostfxr's char_t is wchar_t on Windows and char (UTF-8) elsewhere.
    private static final Charset CHAR_T = WINDOWS ? StandardCharsets.UTF_16LE : StandardCharsets.UTF_8;

    private static final StructLayout INITIALIZE_PARAMETERS = MemoryLayout.structLayout(
            ValueLayout.JAVA_LONG.withName("size"),
            ValueLayout.ADDRESS.withName("host_path"),
            ValueLayout.ADDRESS.withName("dotnet_root"));

    private final MethodHandle attach;

    private DotnetHost(MethodHandle attach) {
        this.attach = attach;
    }

    /** Attaches Wrench.Host to the running JVM; returns {@link #ATTACHED}, {@link #VM_NOT_READY} or a negative error. */
    int attach(String javaHome) throws Throwable {
        try (var arena = Arena.ofConfined()) {
            var utf8 = javaHome.getBytes(StandardCharsets.UTF_8);
            return (int) attach.invokeExact(arena.allocateFrom(javaHome), utf8.length);
        }
    }

    static DotnetHost load(Path dotnetRoot, Path hostDir) throws Throwable {
        var linker = Linker.nativeLinker();
        var fxr = SymbolLookup.libraryLookup(findHostFxr(dotnetRoot), Arena.global());

        var initialize = downcall(linker, fxr, "hostfxr_initialize_for_runtime_config",
                FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.ADDRESS, ValueLayout.ADDRESS, ValueLayout.ADDRESS));
        var getDelegate = downcall(linker, fxr, "hostfxr_get_runtime_delegate",
                FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.ADDRESS, ValueLayout.JAVA_INT, ValueLayout.ADDRESS));
        var close = downcall(linker, fxr, "hostfxr_close",
                FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.ADDRESS));

        try (var arena = Arena.ofConfined()) {
            var parameters = arena.allocate(INITIALIZE_PARAMETERS);
            parameters.set(ValueLayout.JAVA_LONG, 0, INITIALIZE_PARAMETERS.byteSize());
            parameters.set(ValueLayout.ADDRESS, 8, MemorySegment.NULL);
            parameters.set(ValueLayout.ADDRESS, 16, string(arena, dotnetRoot.toString()));

            var runtimeConfig = hostDir.resolve(HOST_ASSEMBLY + ".runtimeconfig.json");
            var handleOut = arena.allocate(ValueLayout.ADDRESS);
            check("hostfxr_initialize_for_runtime_config",
                    (int) initialize.invokeExact(string(arena, runtimeConfig.toString()), parameters, handleOut));
            var handle = handleOut.get(ValueLayout.ADDRESS, 0);

            MemorySegment loadAssembly;
            try {
                var delegateOut = arena.allocate(ValueLayout.ADDRESS);
                check("hostfxr_get_runtime_delegate",
                        (int) getDelegate.invokeExact(handle, HDT_LOAD_ASSEMBLY_AND_GET_FUNCTION_POINTER, delegateOut));
                loadAssembly = delegateOut.get(ValueLayout.ADDRESS, 0);
            } finally {
                // The runtime stays loaded; the context handle is only needed to obtain delegates.
                var unused = (int) close.invokeExact(handle);
            }

            var loadAssemblyFn = linker.downcallHandle(loadAssembly, FunctionDescriptor.of(ValueLayout.JAVA_INT,
                    ValueLayout.ADDRESS, ValueLayout.ADDRESS, ValueLayout.ADDRESS,
                    ValueLayout.ADDRESS, ValueLayout.ADDRESS, ValueLayout.ADDRESS));
            var entryOut = arena.allocate(ValueLayout.ADDRESS);
            check("load_assembly_and_get_function_pointer", (int) loadAssemblyFn.invokeExact(
                    string(arena, hostDir.resolve(HOST_ASSEMBLY + ".dll").toString()),
                    string(arena, ENTRY_TYPE),
                    string(arena, ENTRY_METHOD),
                    UNMANAGEDCALLERSONLY_METHOD,
                    MemorySegment.NULL,
                    entryOut));

            return new DotnetHost(linker.downcallHandle(entryOut.get(ValueLayout.ADDRESS, 0),
                    FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.ADDRESS, ValueLayout.JAVA_INT)));
        }
    }

    private static MethodHandle downcall(Linker linker, SymbolLookup lookup, String name, FunctionDescriptor descriptor) {
        var symbol = lookup.find(name).orElseThrow(() -> new IllegalStateException("hostfxr export not found: " + name));
        return linker.downcallHandle(symbol, descriptor);
    }

    private static MemorySegment string(Arena arena, String value) {
        return arena.allocateFrom(value, CHAR_T);
    }

    private static void check(String function, int rc) {
        // hostfxr status codes are HRESULT-style: negative means failure, 0-2 are success variants.
        if (rc < 0)
            throw new IllegalStateException(String.format("%s failed: 0x%08X", function, rc));
    }

    private static Path findHostFxr(Path dotnetRoot) throws Exception {
        var library = WINDOWS ? "hostfxr.dll" : MAC ? "libhostfxr.dylib" : "libhostfxr.so";
        var fxrDir = dotnetRoot.resolve("host").resolve("fxr");
        try (var versions = Files.list(fxrDir)) {
            return versions
                    .filter(dir -> Files.isRegularFile(dir.resolve(library)))
                    .max(Comparator.comparing(dir -> versionKey(dir.getFileName().toString()), Arrays::compare))
                    .map(dir -> dir.resolve(library))
                    .orElseThrow(() -> new IllegalStateException("No " + library + " under " + fxrDir));
        }
    }

    private static int[] versionKey(String version) {
        return Arrays.stream(version.split("[.-]"))
                .mapToInt(part -> part.chars().allMatch(Character::isDigit) && !part.isEmpty() ? Integer.parseInt(part) : -1)
                .toArray();
    }
}
