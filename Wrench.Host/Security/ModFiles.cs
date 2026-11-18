using System.Security.Cryptography;

namespace Wrench.Host.Security;

/// <summary>
/// A snapshot of every assembly in a mod's folder, read once so that what the player approves is exactly what loads,
/// however the folder changes afterwards.
/// </summary>
internal sealed class ModFiles
{
    private readonly Dictionary<string, byte[]> _files;

    private ModFiles(string directory, Dictionary<string, byte[]> files, string hash)
    {
        Directory = directory;
        _files = files;
        Hash = hash;
    }

    public string Directory { get; }

    /// <summary>SHA-256 over every assembly, relative names included, in a stable order.</summary>
    public string Hash { get; }

    public static ModFiles Capture(string assemblyPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(directory, file);
            var bytes = File.ReadAllBytes(file);
            sha.AppendData(System.Text.Encoding.UTF8.GetBytes(relative + "\n"));
            sha.AppendData(bytes);
            files[relative] = bytes;
        }
        return new ModFiles(directory, files, Convert.ToHexString(sha.GetHashAndReset()));
    }

    public byte[]? Read(string path)
    {
        var relative = Path.GetRelativePath(Directory, Path.GetFullPath(path));
        return _files.GetValueOrDefault(relative);
    }
}
