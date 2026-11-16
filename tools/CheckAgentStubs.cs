// Checks Wrench.Agent's compile-only stubs against a PZ install: dotnet run tools/CheckAgentStubs.cs -- <projectzomboid.jar>
// Run after game updates; a mismatch means the agent would fail to link at runtime.
using System.Diagnostics;
using System.Text.RegularExpressions;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: dotnet run tools/CheckAgentStubs.cs -- <projectzomboid.jar>");
    return 1;
}

var jar = args[0];
var stubsRoot = Path.Combine("Wrench.Agent", "stubs");
var stubClasses = Directory.EnumerateFiles(stubsRoot, "*.java", SearchOption.AllDirectories)
    .Select(f => Path.GetRelativePath(stubsRoot, f)[..^".java".Length].Replace(Path.DirectorySeparatorChar, '.'))
    .Order()
    .ToList();

var compiled = Path.Combine(Path.GetTempPath(), "wrench-stub-check-" + Environment.ProcessId);
Run("javac", ["-d", compiled, .. Directory.EnumerateFiles(stubsRoot, "*.java", SearchOption.AllDirectories)]);

var mismatches = 0;
foreach (var cls in stubClasses)
{
    var real = Members(jar, cls);
    foreach (var member in Members(compiled, cls).Where(m => m.Name != "<init>"))
    {
        var ok = real.Contains(member);
        if (!ok)
            mismatches++;
        Console.WriteLine($"{(ok ? "OK  " : "MISS")} {cls}.{member.Name} {member.Descriptor}{(member.IsStatic ? " static" : "")}");
    }
}
Directory.Delete(compiled, recursive: true);
Console.WriteLine(mismatches == 0 ? "All stub members match." : $"{mismatches} stub member(s) don't match {jar}.");
return mismatches == 0 ? 0 : 2;

static HashSet<(string Name, string Descriptor, bool IsStatic)> Members(string classPath, string cls)
{
    var lines = Run("javap", ["-s", "-cp", classPath, cls]).Split('\n');
    var members = new HashSet<(string, string, bool)>();
    for (var i = 1; i < lines.Length; i++)
    {
        var descriptor = Regex.Match(lines[i], @"^\s+descriptor: (\S+)");
        if (!descriptor.Success)
            continue;
        var declaration = lines[i - 1].Replace(cls + "(", "<init>(");
        var name = Regex.Match(declaration, @"([\w$<>]+)[(;]");
        if (name.Success)
            members.Add((name.Groups[1].Value, descriptor.Groups[1].Value, declaration.Contains(" static ")));
    }
    return members;
}

static string Run(string file, string[] arguments)
{
    var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true })!;
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"{file} exited with {process.ExitCode}");
    return output;
}
