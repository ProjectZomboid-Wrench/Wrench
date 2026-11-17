using System.Reflection;
using System.Runtime.Loader;
using Wrench.Host.Logging;
using Wrench.Host.Security;

namespace Wrench.Host.Mods;

/// <summary>
/// Loads mod assemblies, each into its own load context: Wrench-local mods from <c>wrench/mods/&lt;Name&gt;/&lt;Name&gt;.dll</c>
/// at startup, and assemblies of PZ mods via <see cref="PzMods"/>.
/// </summary>
internal static class ModLoader
{
    private static readonly Logger Log = Logging.Log.For("Wrench.Host");

    public static List<ModInstance> LoadAll(string modsDirectory)
    {
        var loaded = new List<ModInstance>();
        if (!Directory.Exists(modsDirectory))
        {
            Log.Info($"No mods directory at {modsDirectory}");
            return loaded;
        }

        foreach (var directory in Directory.EnumerateDirectories(modsDirectory).Order(StringComparer.Ordinal))
        {
            var id = Path.GetFileName(directory);
            var assemblyPath = Path.Combine(directory, id + ".dll");
            if (!File.Exists(assemblyPath))
            {
                Log.Warn($"Skipping {directory}: no {id}.dll");
                continue;
            }

            loaded.AddRange(LoadAssembly(id, assemblyPath));
        }
        return loaded;
    }

    /// <summary>Loads one mod assembly into its own context and starts every <see cref="ModInstance"/> in it.</summary>
    /// <param name="vet">False only for mods the player has granted full trust.</param>
    public static List<ModInstance> LoadAssembly(string id, string assemblyPath, bool vet = true)
    {
        var started = new List<ModInstance>();
        var violations = vet ? ModVetting.Vet(assemblyPath) : [];
        if (violations.Count > 0)
        {
            Log.Error($"Rejected {id}: it uses things mods aren't allowed to:{Environment.NewLine}  - " +
                      string.Join(Environment.NewLine + "  - ", violations) + Environment.NewLine +
                      $"  A mod that needs this can declare {ModPermissions.ModInfoKey}={ModPermissions.Full} in its " +
                      "mod.info; players are then asked whether to allow it.");
            return started;
        }

        Assembly assembly;
        Type[] modTypes;
        try
        {
            assembly = new ModLoadContext(id, assemblyPath).LoadFromAssemblyPath(assemblyPath);
            modTypes = assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(ModInstance)))
                .ToArray();
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load {assemblyPath}", e);
            return started;
        }

        foreach (var type in modTypes)
        {
            try
            {
                var mod = (ModInstance)Activator.CreateInstance(type)!;
                mod.Initialize(ModHost.Instance, id);
                mod.OnLoad();
                started.Add(mod);
                Log.Info($"Loaded {id} ({type.FullName})");
            }
            catch (Exception e)
            {
                Log.Error($"Failed to start {id} ({type.FullName})", e);
            }
        }
        if (modTypes.Length == 0)
            Log.Warn($"{assemblyPath} contains no ModInstance subclasses");

        try
        {
            Zomboid.ContextMenus.ContextMenuRegistry.Discover(assembly, started);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to set up {id}'s context menus", e);
        }
        return started;
    }

    private sealed class ModLoadContext(string id, string assemblyPath) : AssemblyLoadContext(id)
    {
        private static readonly Assembly[] SharedAssemblies = [typeof(ModInstance).Assembly, typeof(Zomboid.JavaObject).Assembly];
        private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

        protected override Assembly? Load(AssemblyName name)
        {
            // Every mod must bind to the host's Wrench.Api and Wrench.Game, or ModInstance (and every game wrapper)
            // would be a different type per mod.
            var shared = SharedAssemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
            if (shared is not null)
                return shared;
            var path = _resolver.ResolveAssemblyToPath(name);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
