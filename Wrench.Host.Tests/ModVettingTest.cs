using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wrench.Host.Security;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Wrench.Host.Tests;

[TestClass]
public class ModVettingTest
{
    private const string Usings = "using System; using System.Collections.Generic; using System.Linq; using Wrench;\n";

    [TestMethod]
    public void Ordinary_Mod_Code_Passes()
    {
        var violations = Vet("""
            public sealed class Clean : ModInstance
            {
                public override void OnLoad() => RegisterLuaEvent(LuaEvents.OnGameBoot, e =>
                {
                    var player = Lua.Call("getPlayer");
                    if (player is null)
                        return;
                    double hours = player.getHoursSurvived();
                    var names = new List<string> { "a", "b" }.Where(n => n.Length > 0).Select(n => n.ToUpperInvariant()).ToList();
                    var counts = new Dictionary<string, int>();
                    foreach (var n in names)
                        counts[n] = counts.GetValueOrDefault(n) + 1;
                    Log.Info($"{hours:F1} {string.Join(",", names)} {Math.Max(1, 2)} {nameof(Clean)}");
                    try { throw new InvalidOperationException("x"); }
                    catch (InvalidOperationException ex) { Log.Warn(ex.Message); }
                    Lua.Replace("Tutorial1.lockDoor", (int x, int y) => x + y);
                });
            }
            """);

        Assert.AreEqual("", string.Join(" | ", violations));
    }

    [TestMethod]
    [DataRow("System.IO.File.WriteAllText(\"x\", \"y\");", "uses System.IO.File")]
    [DataRow("System.Diagnostics.Process.Start(\"sh\");", "uses System.Diagnostics.Process")]
    [DataRow("using var client = new System.Net.Http.HttpClient();", "uses System.Net.Http.HttpClient")]
    [DataRow("new System.Threading.Thread(() => { }).Start();", "uses System.Threading.Thread")]
    [DataRow("Environment.Exit(1);", "uses System.Environment")]
    [DataRow("var asm = \"\".GetType().Assembly;", "uses System.Object::GetType")]
    [DataRow("var methods = typeof(string).GetMethods();", "uses System.Type::GetMethods")]
    [DataRow("Action a = () => { }; var m = a.Method;", "::get_Method")]
    [DataRow("try { } catch (Exception e) { var site = e.TargetSite; }", "::get_TargetSite")]
    [DataRow("var r = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(null!);", "RuntimeHelpers::GetUninitializedObject")]
    [DataRow("var p = System.Runtime.InteropServices.Marshal.AllocHGlobal(8);", "uses System.Runtime.InteropServices.Marshal")]
    [DataRow("dynamic s = \"x\"; var t = s.GetType();", "uses dynamic member GetType")]
    [DataRow("dynamic t = typeof(string); var m = t.GetMethods();", "uses dynamic member GetMethods")]
    [DataRow("dynamic e = new Exception(); var site = e.TargetSite;", "uses dynamic member TargetSite")]
    public void Escapes_Are_Rejected(string body, string expected)
    {
        var violations = Vet(Mod(body));

        Assert.IsTrue(violations.Any(v => v.Contains(expected)), $"expected '{expected}' in: {string.Join(" | ", violations)}");
        Console.WriteLine($"Violations: \n{string.Join("\n", violations)}");
    }

    [TestMethod]
    public void Native_Methods_Are_Rejected()
    {
        var violations = Vet(Mod("") + """

            public static class Native
            {
                [System.Runtime.InteropServices.DllImport("libc")]
                public static extern int getpid();
            }
            """);

        Assert.IsTrue(violations.Any(v => v.Contains("P/Invoke")), string.Join(" | ", violations));
    }

    [TestMethod]
    public void Unsafe_Code_Fails_IL_Verification()
    {
        var violations = ModVetting.Vet(Compile(Mod("unsafe { int x = 1; int* p = &x; *p = 2; }"), "TestMod", allowUnsafe: true));

        Assert.IsTrue(violations.Any(v => v.Contains("unverifiable IL")), string.Join(" | ", violations));
    }

    [TestMethod]
    public void Type_Forwarders_Are_Rejected()
    {
        var violations = Vet("[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(System.IO.File))]\n" + Mod(""));

        Assert.IsTrue(violations.Any(v => v.Contains("forwards type System.IO.File")), string.Join(" | ", violations));
    }

    [TestMethod]
    public void Shipping_An_Assembly_Twice_Is_Rejected()
    {
        var path = Compile(Mod(""), "Twice");
        var nested = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(path)!, "nested")).FullName;
        File.Copy(path, Path.Combine(nested, "Twice.dll"));

        var violations = ModVetting.Vet(path);

        Assert.IsTrue(violations.Any(v => v.Contains("ships Twice.dll more than once")), string.Join(" | ", violations));
    }

    [TestMethod]
    public void Shipping_A_Framework_Named_Assembly_Is_Rejected()
    {
        var path = Compile(Mod(""), "Shadowing");
        File.Copy(path, Path.Combine(Path.GetDirectoryName(path)!, "System.Runtime.dll"));

        var violations = ModVetting.Vet(path);

        Assert.IsTrue(violations.Any(v => v.Contains("ships System.Runtime.dll")), string.Join(" | ", violations));
    }

    [TestMethod]
    [DataRow("Wrench.Samples.ReallyCDDAy", "pzmod/WrenchReallyCDDAy/42/dotnet/Wrench.Samples.ReallyCDDAy.dll")]
    [DataRow("Wrench.Samples.GourmetDishes", "pzmod/WrenchGourmetDishes/42/dotnet/Wrench.Samples.GourmetDishes.dll")]
    [DataRow("Wrench.Samples.Tutorial", "Wrench.Samples.Tutorial.dll")]
    public void Sample_Mods_Pass(string project, string assembly)
    {
        var configuration = AppContext.BaseDirectory.Contains("/Release/") ? "Release" : "Debug";
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            project, "bin", configuration, "net11.0", assembly));

        Assert.AreEqual("", string.Join(" | ", ModVetting.Vet(path)));
    }

    private static string Mod(string onLoadBody) =>
        $$"""
        public sealed class TestMod : ModInstance
        {
            public override void OnLoad()
            {
                {{onLoadBody}}
            }
        }
        """;

    private static List<string> Vet(string source) => ModVetting.Vet(Compile(source, "TestMod"));

    private static string Compile(string source, string name, bool allowUnsafe = false)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(ModInstance).Assembly.Location)
            .Distinct()
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(Usings + source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe,
                nullableContextOptions: NullableContextOptions.Enable));

        var directory = Directory.CreateTempSubdirectory("wrench-vetting-").FullName;
        var path = Path.Combine(directory, name + ".dll");
        var result = compilation.Emit(path);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }
}
