using System.Reflection;
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
    /// <param name="approved">The snapshot the player granted full trust to; loaded unvetted, and only from it.</param>
    public static List<ModInstance> LoadAssembly(string id, string assemblyPath, ModFiles? approved = null)
    {
        var started = new List<ModInstance>();
        var violations = approved is null ? ModVetting.Vet(assemblyPath) : [];
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
            var context = approved is null
                ? new ModLoadContext(id, assemblyPath, restricted: true)
                : new ModLoadContext(id, assemblyPath, approved);
            assembly = context.LoadMod(assemblyPath);
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
            Zomboid.Lua.ContextMenus.ContextMenuRegistry.Discover(assembly, started);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to set up {id}'s context menus", e);
        }
        return started;
    }
}
