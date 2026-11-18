using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wrench.Host.Security;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Wrench.Host.Tests;

[TestClass]
public class ModVettingTest
{
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
    [DataRow("System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(\"de-DE\");", "::set_CurrentCulture")]
    [DataRow("System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;", "::set_DefaultThreadCurrentCulture")]
    [DataRow("System.Text.Encoding.RegisterProvider(null!);", "::RegisterProvider")]
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
    [DataRow("string name", "name", "isn't a string literal")]
    [DataRow("bool flag", "flag ? \"Name\" : \"Assembly\"", "isn't a string literal")]
    public void Binder_Member_Names_Must_Be_Literals(string parameter, string nameExpression, string expected)
    {
        var violations = Vet($$"""
            using Microsoft.CSharp.RuntimeBinder;
            using System.Runtime.CompilerServices;
            public sealed class TestMod : ModInstance
            {
                public override void OnLoad() { }
                private static object Read(object target, {{parameter}})
                {
                    var binder = Binder.GetMember(CSharpBinderFlags.None, {{nameExpression}}, typeof(TestMod),
                        new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) });
                    var site = CallSite<Func<CallSite, object, object>>.Create(binder);
                    return site.Target(site, target);
                }
            }
            """);

        Assert.IsTrue(violations.Any(v => v.Contains(expected)), string.Join(" | ", violations));
    }

    [TestMethod]
    public void Binder_Methods_As_Delegates_Are_Rejected()
    {
        var violations = Vet("using Microsoft.CSharp.RuntimeBinder;\n" + Mod(
            "Func<CSharpBinderFlags, string, Type, IEnumerable<CSharpArgumentInfo>, System.Runtime.CompilerServices.CallSiteBinder> f = Binder.GetMember;"));

        Assert.IsTrue(violations.Any(v => v.Contains("uses Microsoft.CSharp.RuntimeBinder.Binder::GetMember")), string.Join(" | ", violations));
    }

    [TestMethod]
    public void Records_And_Type_Comparisons_Pass()
    {
        var violations = Vet(Mod("var same = typeof(string) == typeof(object) || new Point(1, 2) == new Point(1, 2);") +
                             "\npublic sealed record Point(int X, int Y);");

        Assert.IsEmpty(violations, string.Join(" | ", violations));
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
        var violations = ModVetting.Vet(ModCompiler.Compile(Mod("unsafe { int x = 1; int* p = &x; *p = 2; }"), "TestMod", allowUnsafe: true));

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
        var path = ModCompiler.Compile(Mod(""), "Twice");
        var nested = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(path)!, "nested")).FullName;
        File.Copy(path, Path.Combine(nested, "Twice.dll"));

        var violations = ModVetting.Vet(path);

        Assert.IsTrue(violations.Any(v => v.Contains("ships Twice.dll more than once")), string.Join(" | ", violations));
    }

    [TestMethod]
    public void Shipping_A_Framework_Named_Assembly_Is_Rejected()
    {
        var path = ModCompiler.Compile(Mod(""), "Shadowing");
        File.Copy(path, Path.Combine(Path.GetDirectoryName(path)!, "System.Runtime.dll"));

        var violations = ModVetting.Vet(path);

        Assert.IsTrue(violations.Any(v => v.Contains("ships System.Runtime.dll")), string.Join(" | ", violations));
    }

    [TestMethod]
    [DataRow("""
        namespace System.Runtime.CompilerServices
        {
            [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class IgnoresAccessChecksToAttribute(string assemblyName) : Attribute { }
        }
        """, "defines System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute in a framework namespace")]
    [DataRow("""
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        public sealed class Overlap
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public string? Text;
            [System.Runtime.InteropServices.FieldOffset(0)] public object[]? Items;
        }
        """, "defines Overlap with explicit layout")]
    [DataRow("""
        public static class Accessor
        {
        #pragma warning disable CS0626
            public static extern int Peek(string s);
        #pragma warning restore CS0626
        }
        """, "declares extern method Peek")]
    [DataRow("""
        public static class UsesHost
        {
            public static object Binder() => Wrench.Host.Security.DynamicBinder.Convert(default, typeof(int), typeof(UsesHost));
        }
        """, "references assembly Wrench.Host")]
    public void Escape_Hatches_In_Definitions_Are_Rejected(string extra, string expected)
    {
        var violations = Vet(Mod("") + "\n" + extra);

        Assert.IsTrue(violations.Any(v => v.Contains(expected)), string.Join(" | ", violations));
    }

    [TestMethod]
    public void An_Assembly_Under_Another_Name_Is_Rejected()
    {
        var violations = ModVetting.Vet(ModCompiler.Compile(Mod(""), "Real", fileName: "Disguised"));

        Assert.IsTrue(violations.Any(v => v.Contains("is assembly Real in a file named Disguised.dll")), string.Join(" | ", violations));
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

        var violations = ModVetting.Vet(path);

        Assert.IsEmpty(violations, string.Join(" | ", violations));
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

    private static List<string> Vet(string source) => ModVetting.Vet(ModCompiler.Compile(source, "TestMod"));
}
