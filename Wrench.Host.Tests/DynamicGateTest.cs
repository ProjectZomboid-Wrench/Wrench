using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wrench.Host.Mods;
using Wrench.Host.Security;

namespace Wrench.Host.Tests;

[TestClass]
public class DynamicGateTest
{
    [TestMethod]
    [DataRow("dynamic s = \"abc\"; return (int)s.Length;", 3)]
    [DataRow("dynamic s = \"abc\"; return (int)s.IndexOf('c');", 2)]
    [DataRow("dynamic list = new List<int> { 1, 2 }; list.Add(3); return (int)list.Count;", 3)]
    [DataRow("dynamic a = 2; dynamic b = 3.5; return (int)(a * b);", 7)]
    [DataRow("dynamic own = new Own(); own.Value = 4; return (int)own.Twice();", 8)]
    [DataRow("Func<int, int> f = x => x * 2; dynamic g = f; return (int)g(5);", 10)]
    public void Allowed_Dynamic_Code_Runs(string body, int expected)
    {
        Assert.AreEqual(expected, Run(body));
    }

    [TestMethod]
    // Passes vetting (TypeCode isn't reflective), but its signature uses a type mods may not reference.
    [DataRow("dynamic s = \"x\"; return (int)s.GetTypeCode();", "GetTypeCode")]
    [DataRow("dynamic e = new ArgumentException(\"x\"); object m = e.TargetSite; return 0;", "TargetSite")]
    [DataRow("dynamic t = typeof(string); object a = t.Assembly; return 0;", "Assembly")]
    [DataRow("dynamic t = typeof(string); return ((string)t.Name).Length;", "Name")]
    public void Denied_Members_Throw_When_Bound(string body, string member)
    {
        var e = Assert.ThrowsExactly<TargetInvocationException>(() => Run(body, vet: false));

        Assert.IsInstanceOfType<Microsoft.CSharp.RuntimeBinder.RuntimeBinderException>(e.InnerException);
        StringAssert.Contains(e.InnerException!.Message, member);
    }

    [TestMethod]
    public void Static_Vetting_Alone_Misses_What_The_Gate_Catches()
    {
        var violations = ModVetting.Vet(ModCompiler.Compile(Probe("dynamic s = \"x\"; return (int)s.GetTypeCode();"), "GateMod"));

        Assert.IsEmpty(violations, string.Join(" | ", violations));
    }

    [TestMethod]
    public void A_Forged_Binder_Context_Is_Refused()
    {
        var e = Assert.ThrowsExactly<TargetInvocationException>(() => Run("""
            var binder = Microsoft.CSharp.RuntimeBinder.Binder.GetMember(Microsoft.CSharp.RuntimeBinder.CSharpBinderFlags.None,
                "Length", typeof(ModInstance), new[] { Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo.Create(
                    Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfoFlags.None, null) });
            return 0;
            """));

        StringAssert.Contains(e.InnerException!.Message, "must be created from mod code");
    }

    [TestMethod]
    public void Mods_Without_Dynamic_Are_Loaded_Unchanged()
    {
        Assert.AreEqual(1, Run("return 1;"));
    }

    private static int Run(string body, bool vet = true)
    {
        var path = ModCompiler.Compile(Probe(body), "GateMod");
        if (vet)
        {
            var violations = ModVetting.Vet(path);
            Assert.IsEmpty(violations, string.Join(" | ", violations));
        }

        var assembly = new ModLoadContext("GateMod", path, restricted: true).LoadMod(path);
        return (int)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
    }

    private static string Probe(string body) => $$"""
            public sealed class TestMod : ModInstance { public override void OnLoad() { } }
            public sealed class Own
            {
                public int Value { get; set; }
                public int Twice() => Value * 2;
            }
            public static class Probe
            {
                public static int Run()
                {
                    {{body}}
                }
            }
            """;
}
