using System.Security.Cryptography;
using System.Text.Json;

namespace Wrench.Host.Security;

/// <summary>
/// The player's permission decisions, kept in PZ's user folder rather than any mod folder so a mod can't grant
/// itself access. A decision only applies to the exact code it was made for: any change to the mod's assemblies asks
/// again, since new code could do anything with full trust.
/// </summary>
internal sealed class TrustStore(string path)
{
    public enum Decision
    {
        Undecided,
        Allowed,
        Denied,
    }

    private sealed record Entry(bool Allowed, string Hash, string[] Permissions, DateTimeOffset DecidedAt);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Path => path;

    public Decision Lookup(string modId, string hash, IReadOnlyCollection<string> permissions)
    {
        if (!Load().TryGetValue(modId, out var entry) || entry.Hash != hash || !permissions.All(entry.Permissions.Contains))
            return Decision.Undecided;
        return entry.Allowed ? Decision.Allowed : Decision.Denied;
    }

    public void Record(string modId, bool allowed, string hash, IReadOnlyCollection<string> permissions)
    {
        var entries = Load();
        entries[modId] = new Entry(allowed, hash, permissions.ToArray(), DateTimeOffset.UtcNow);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>SHA-256 over every assembly in the mod's folder, names included, in a stable order.</summary>
    public static string HashMod(string assemblyPath)
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(assemblyPath))!;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            sha.AppendData(System.Text.Encoding.UTF8.GetBytes(System.IO.Path.GetRelativePath(directory, file) + "\n"));
            sha.AppendData(File.ReadAllBytes(file));
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    // A missing or unreadable file means nothing has been decided; the player is simply asked again.
    private Dictionary<string, Entry> Load()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path)) ?? new()
                : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }
}
