using System.Reflection;
using System.Runtime.Loader;
using Wrench.Host.Security;

namespace Wrench.Host.Mods;

/// <summary>
/// One mod's assemblies. A restricted mod resolves only the DLLs vetting saw in its folder, each with its binder calls
/// rewritten (<see cref="BinderRewriter"/>); a fully trusted one resolves through its deps.json like any app, but only
/// to assemblies in the <see cref="ModFiles"/> snapshot the player approved.
/// </summary>
internal sealed class ModLoadContext : AssemblyLoadContext
{
    private static readonly Assembly[] SharedAssemblies =
    [
        typeof(ModInstance).Assembly, typeof(Zomboid.Java.JavaObject).Assembly, typeof(Zomboid.Lua.LuaObject).Assembly,
        typeof(DynamicBinder).Assembly,
    ];

    private readonly Dictionary<string, string>? _vetted;
    private readonly AssemblyDependencyResolver? _resolver;
    private readonly ModFiles? _approved;

    public ModLoadContext(string id, string assemblyPath, bool restricted) : base(id) =>
        _vetted = restricted ? ModVetting.ModAssemblies(Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!) : null;

    public ModLoadContext(string id, string assemblyPath, ModFiles approved) : base(id)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
        _approved = approved;
    }

    public Assembly LoadMod(string assemblyPath) =>
        _vetted is null ? LoadApproved(assemblyPath) : LoadRestricted(Path.GetFullPath(assemblyPath));

    protected override Assembly? Load(AssemblyName name)
    {
        // Every mod must bind to the host's Wrench.Api and Wrench.Game.*, or ModInstance (and every game wrapper)
        // would be a different type per mod. Wrench.Host is only referenced by rewritten binder calls.
        var shared = SharedAssemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
        if (shared is not null)
            return shared;
        if (_vetted is not null)
            return name.Name is not null && _vetted.TryGetValue(name.Name, out var vetted) ? LoadRestricted(vetted) : null;
        var path = _resolver?.ResolveAssemblyToPath(name);
        return path is null ? null : LoadApproved(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }

    private Assembly LoadApproved(string path)
    {
        if (_approved is null)
            return LoadFromAssemblyPath(path);
        var bytes = _approved.Read(path) ?? throw new FileLoadException($"{path} isn't among the approved assemblies");
        using var stream = new MemoryStream(bytes);
        return LoadFromStream(stream);
    }

    private Assembly LoadRestricted(string path)
    {
        if (BinderRewriter.Rewrite(path) is not var (assembly, symbols))
            return LoadFromAssemblyPath(path);
        using var assemblyStream = new MemoryStream(assembly);
        using var symbolStream = symbols is null ? null : new MemoryStream(symbols);
        return LoadFromStream(assemblyStream, symbolStream);
    }
}
