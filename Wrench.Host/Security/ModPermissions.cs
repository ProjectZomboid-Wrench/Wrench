namespace Wrench.Host.Security;

/// <summary>
/// Permissions a PZ mod declares with <c>dotnetPermissions=</c> in mod.info. Only <see cref="Full"/> exists so far;
/// scoped permissions enforced by Wrench.Api (own storage, specific hosts) will join it.
/// </summary>
internal static class ModPermissions
{
    public const string ModInfoKey = "dotnetPermissions";
    public const string Full = "full";

    // Shown to players as-is; keep them plain and honest about the risk.
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        [Full] = "Full access to your computer: it can read and change any of your files, run programs and connect " +
                 "to the internet. Only allow this for mods from authors you trust.",
    };

    public static (List<string> Known, List<string> Unknown) Parse(string? value)
    {
        var names = (value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return (names.Where(Descriptions.ContainsKey).ToList(), names.Where(n => !Descriptions.ContainsKey(n)).ToList());
    }

    public static string Describe(string permission) => Descriptions[permission];
}
