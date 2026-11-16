using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using ILVerify;

namespace Wrench.Host.Security;

/// <summary>
/// Decides whether a mod may load, before any of its code runs: every assembly in the mod's folder must pass
/// <see cref="AssemblyInspector"/> and IL verification. Modern .NET has no in-process sandbox, so this is the line.
/// </summary>
internal static class ModVetting
{
    private const int MaxReported = 10;

    private static readonly string FrameworkDirectory = RuntimeEnvironment.GetRuntimeDirectory();
    private static readonly string[] HostAssemblies = ["Wrench.Api", "Wrench.Game", "Wrench.Host", "JNIBinding", "ILVerification"];

    public static List<string> Vet(string assemblyPath)
    {
        var modDirectory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!;
        var modFiles = Directory.EnumerateFiles(modDirectory, "*.dll", SearchOption.AllDirectories)
            .GroupBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Two copies of one assembly name: the scan couldn't know which the runtime will load.
        var duplicates = modFiles.Where(g => g.Count() > 1).Select(g => $"ships {g.Key}.dll more than once").ToList();
        if (duplicates.Count > 0)
            return duplicates;
        var modAssemblies = modFiles.ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);
        var frameworkAssemblies = Directory.EnumerateFiles(FrameworkDirectory, "*.dll")
            .ToDictionary(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase);

        // A mod-shipped System.Runtime.dll would be scanned as mod code yet resolve to the real one at runtime.
        var violations = modAssemblies.Keys
            .Where(name => frameworkAssemblies.ContainsKey(name) || HostAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Select(name => $"ships {name}.dll, which shadows a framework or Wrench assembly")
            .ToList();
        if (violations.Count > 0)
            return violations;

        var inspector = new AssemblyInspector(modAssemblies.Keys.ToHashSet(StringComparer.Ordinal));
        using var resolver = new Resolver(frameworkAssemblies, modAssemblies);
        var verifier = new Verifier(resolver, new VerifierOptions { SanityChecks = true });
        verifier.SetSystemModuleName(new AssemblyNameInfo("System.Private.CoreLib"));

        foreach (var (name, path) in modAssemblies)
        {
            violations.AddRange(inspector.Inspect(path).Select(v => $"{name}: {v}"));
            violations.AddRange(Verify(verifier, resolver, name).Select(v => $"{name}: {v}"));
        }
        return violations.Distinct().Take(MaxReported).ToList();
    }

    private static IEnumerable<string> Verify(Verifier verifier, Resolver resolver, string name)
    {
        var pe = resolver.Open(name)!;
        var md = pe.GetMetadataReader();
        foreach (var result in verifier.Verify(pe))
        {
            var method = result.Method.IsNil ? "" : $" in {md.GetString(md.GetMethodDefinition(result.Method).Name)}";
            yield return $"unverifiable IL ({result.Code}){method}";
        }
    }

    private sealed class Resolver(Dictionary<string, string> framework, Dictionary<string, string> mod) : IResolver, IDisposable
    {
        private readonly Dictionary<string, PEReader> _open = new(StringComparer.OrdinalIgnoreCase);

        public PEReader? ResolveAssembly(AssemblyNameInfo assemblyName) => Open(assemblyName.Name);

        public PEReader? ResolveModule(AssemblyNameInfo referencingAssembly, string fileName) => null;

        public PEReader? Open(string name)
        {
            if (_open.TryGetValue(name, out var reader))
                return reader;
            var path = mod.GetValueOrDefault(name) ?? framework.GetValueOrDefault(name)
                       ?? name switch
                       {
                           "Wrench.Api" => typeof(ModInstance).Assembly.Location,
                           "Wrench.Game" => typeof(Zomboid.JavaObject).Assembly.Location,
                           _ => null,
                       };
            if (path is null)
                return null;
            return _open[name] = new PEReader(File.OpenRead(path));
        }

        public void Dispose()
        {
            foreach (var reader in _open.Values)
                reader.Dispose();
        }
    }
}
