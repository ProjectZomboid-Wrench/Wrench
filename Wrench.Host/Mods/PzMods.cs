using Wrench.Host.Events;
using Wrench.Host.Logging;
using Wrench.Host.Lua;
using Wrench.Host.Security;

namespace Wrench.Host.Mods;

/// <summary>
/// Loads .NET assemblies shipped inside regular PZ mods (local or Workshop). A mod opts in with a <c>mod.info</c> line
/// such as <c>dotnet=dotnet/MyMod.dll</c>, resolved against the mod's version folder (e.g. <c>42/</c>) and then
/// <c>common/</c>, the same layering PZ uses for its own files. Mods declaring <c>dotnetPermissions=</c> wait for the
/// player's decision, asked by the agent's <see cref="PermissionPrompt"/> game state, which Lua can't reach.
/// </summary>
internal static class PzMods
{
    public const string ModInfoKey = "dotnet";

    private sealed record PendingMod(string Id, string Name, string AssemblyPath, string Hash, List<string> Permissions);

    private static readonly Logger Log = Logging.Log.For("Wrench.Host");
    private static readonly Dictionary<string, List<ModInstance>> Loaded = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PendingMod> Pending = new(StringComparer.Ordinal);
    private static TrustStore? _trust;

    /// <summary>
    /// Runs on every OnGameBoot, which PZ fires after (re)loading the active mods and their Lua: starts newly enabled
    /// .NET mods, queues the ones needing permission, and toggles <see cref="ModInstance.IsActive"/> for loaded ones.
    /// </summary>
    public static void Sync(ILuaHost lua)
    {
        _trust ??= new TrustStore(Path.Combine((string)Call(lua, "getMyDocumentFolder")!, "Wrench", "trust.json"));
        Pending.Clear();
        PermissionPrompt.Clear();

        var active = new HashSet<string>(StringComparer.Ordinal);
        dynamic activated = Call(lua, "getActivatedMods")!;
        for (var i = 0; i < activated.size(); i++)
        {
            string id = activated.get(i);
            active.Add(id);
            if (!Loaded.ContainsKey(id) && Call(lua, "getModInfoByID", id) is GameObject info)
                Start(id, info);
        }

        foreach (var (id, mods) in Loaded)
        {
            var isActive = active.Contains(id);
            foreach (var mod in mods.Where(m => m.IsActive != isActive))
            {
                mod.IsActive = isActive;
                Log.Info($"{(isActive ? "Re-enabled" : "Disabled")} .NET mod {id} ({mod.GetType().FullName})");
            }
        }
        foreach (var mod in Pending.Values)
            PermissionPrompt.Request(mod.Id, mod.Name, mod.Permissions.Select(ModPermissions.Describe).ToList());
    }

    private static void Start(string id, GameObject modInfoObject)
    {
        dynamic info = modInfoObject;
        string? versionDir = info.getVersionDir();
        string? commonDir = info.getCommonDir();
        string? modDir = info.getDir();
        var folders = new[] { versionDir, commonDir, modDir }.OfType<string>().Distinct().ToArray();
        var modInfo = folders.Select(f => Path.Combine(f, "mod.info")).FirstOrDefault(File.Exists);
        var keys = modInfo is null ? new Dictionary<string, string>() : ReadKeys(modInfo);
        if (!keys.TryGetValue(ModInfoKey, out var entry))
            return;

        var assemblyPath = folders.Select(f => Path.GetFullPath(Path.Combine(f, entry))).FirstOrDefault(File.Exists);
        if (assemblyPath is null)
        {
            Log.Error($"{id}: {ModInfoKey}={entry} not found under {string.Join(" or ", folders)}");
            return;
        }

        var (permissions, unknown) = ModPermissions.Parse(keys.GetValueOrDefault(ModPermissions.ModInfoKey));
        if (unknown.Count > 0)
        {
            Log.Error($"Rejected {id}: unknown {ModPermissions.ModInfoKey} {string.Join(", ", unknown)}");
            return;
        }
        if (permissions.Count == 0)
        {
            Log.Info($"Loading .NET mod {id} from {assemblyPath}");
            Loaded[id] = ModLoader.LoadAssembly(id, assemblyPath);
            return;
        }

        var hash = TrustStore.HashMod(assemblyPath);
        switch (_trust!.Lookup(id, hash, permissions))
        {
            case TrustStore.Decision.Allowed:
                Log.Info($"Loading .NET mod {id} from {assemblyPath} with {string.Join(", ", permissions)} (approved earlier)");
                Loaded[id] = ModLoader.LoadAssembly(id, assemblyPath, vet: false);
                break;
            case TrustStore.Decision.Denied:
                Log.Info($"Not loading .NET mod {id}: the player declined its permissions");
                break;
            default:
                Log.Info($".NET mod {id} is waiting for the player to approve {string.Join(", ", permissions)}");
                Pending[id] = new PendingMod(id, info.getName() ?? id, assemblyPath, hash, permissions);
                break;
        }
    }

    /// <summary>The player's answer from the prompt: recorded, and if allowed the mod starts now.</summary>
    public static bool Decide(string id, bool allow)
    {
        if (!Pending.Remove(id, out var mod))
            return false;
        _trust!.Record(id, allow, mod.Hash, mod.Permissions);
        if (!allow)
        {
            Log.Info($"The player declined {string.Join(", ", mod.Permissions)} for .NET mod {id}");
            return true;
        }

        Log.Info($"The player allowed {string.Join(", ", mod.Permissions)} for .NET mod {id}; loading it");
        var mods = ModLoader.LoadAssembly(id, mod.AssemblyPath, vet: false);
        Loaded[id] = mods;
        // OnGameBoot already ran for this Lua load, so run the new mod's PatchLua callbacks now.
        LuaEventRouter.InvokeNow(LuaEvents.OnGameBoot, mods);
        return true;
    }

    private static Dictionary<string, string> ReadKeys(string modInfo)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(modInfo))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length > 0)
                keys.TryAdd(key, value);
        }
        return keys;
    }

    private static object? Call(ILuaHost lua, string function, params object?[] args)
    {
        var target = lua.GetGlobal(function) ?? throw new LuaException($"global '{function}' is nil");
        var results = lua.Call(null, target, args);
        return results.Length > 0 ? results[0] : null;
    }
}
