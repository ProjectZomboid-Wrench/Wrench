using System.Reflection;
using System.Runtime.Loader;
using Wrench.Host.Logging;

namespace Wrench.Host.Mods;

/// <summary>Loads <c>mods/&lt;Name&gt;/&lt;Name&gt;.dll</c>, each into its own load context.</summary>
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

            Type[] modTypes;
            try
            {
                var assembly = new ModLoadContext(id, assemblyPath).LoadFromAssemblyPath(assemblyPath);
                modTypes = assembly.GetTypes()
                    .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(ModInstance)))
                    .ToArray();
            }
            catch (Exception e)
            {
                Log.Error($"Failed to load {assemblyPath}", e);
                continue;
            }

            foreach (var type in modTypes)
            {
                try
                {
                    var mod = (ModInstance)Activator.CreateInstance(type)!;
                    mod.Initialize(ModHost.Instance, id);
                    mod.OnLoad();
                    loaded.Add(mod);
                    Log.Info($"Loaded {id} ({type.FullName})");
                }
                catch (Exception e)
                {
                    Log.Error($"Failed to start {id} ({type.FullName})", e);
                }
            }
        }
        return loaded;
    }

    private sealed class ModLoadContext(string id, string assemblyPath) : AssemblyLoadContext(id)
    {
        private static readonly Assembly ApiAssembly = typeof(ModInstance).Assembly;
        private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

        protected override Assembly? Load(AssemblyName name)
        {
            // Every mod must bind to the host's Wrench.Api, or ModInstance would be a different type per mod.
            if (name.Name == ApiAssembly.GetName().Name)
                return ApiAssembly;
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
