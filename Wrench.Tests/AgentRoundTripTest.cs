using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace Wrench.Tests;

[TestClass]
public class AgentRoundTripTest
{
    [TestMethod]
    public void Agent_Boots_Host_And_Java_Calls_Into_Dotnet()
    {
        var output = RunHello();

        StringAssert.Contains(output, "[Wrench.Host] INFO: Attached to");
        StringAssert.Contains(output, "[Wrench.Host] INFO: hello from Java");
        StringAssert.Contains(output, "Hello.main ran");
    }

    [TestMethod]
    public void Mods_Receive_Lua_Events_As_Dotnet_Values()
    {
        var output = RunHello();

        StringAssert.Contains(output, "[Wrench.Host] INFO: Loaded Wrench.Tests.SampleMod (Wrench.Tests.SampleMod.SampleMod)");
        StringAssert.Contains(output, "[Wrench.Tests.SampleMod] INFO: OnGameBoot(hi, 42, null, java.lang.StringBuilder:sb)");
        StringAssert.Contains(output, "[Wrench.Tests.SampleMod] INFO: second arg as double: 42");
        StringAssert.Contains(output, "[Wrench.Tests.SampleMod] INFO: kept GameObject expired as expected");
    }

    [TestMethod]
    public void Failing_Mod_Handler_Does_Not_Break_Other_Mods_Or_The_Game()
    {
        var output = RunHello();

        StringAssert.Contains(output, "[Wrench.Tests.SampleMod] ERROR: OnGameBoot handler failed");
        StringAssert.Contains(output, "System.InvalidOperationException: boom");
        StringAssert.Contains(output, "[Wrench.Tests.SampleMod] INFO: OnGameBoot(");
        StringAssert.Contains(output, "Event.trigger ran: OnGameBoot");
    }

    [TestMethod]
    public void Unsubscribed_Events_Are_Not_Forwarded()
    {
        var output = RunHello();

        StringAssert.Contains(output, "Event.trigger ran: OnTick");
        Assert.DoesNotContain("OnTick(", output);
    }

    [TestMethod]
    public void Mods_Build_Lua_Tables_And_Functions_That_Lua_Calls()
    {
        var output = RunHello();

        StringAssert.Contains(output, "challenge id=WrenchTest name=Wrench test challenge");
        StringAssert.Contains(output, "spawn x=10788.0 z=0.0");
        StringAssert.Contains(output, "Zombies=1.0 PopulationMultiplier=2.5 XPMultiplierGlobal=1.0 XPMultiplierGlobalToggle=true");
        StringAssert.Contains(output, "Add -> 42.0");
    }

    [TestMethod]
    public void Mods_Call_Java_Objects_Dynamically_From_Lua_Callbacks()
    {
        var output = RunHello();

        StringAssert.Contains(output, "[Wrench.Tests.SampleMod] INFO: AddPlayer(0) rooms=1 player=FakePlayer");
        StringAssert.Contains(output, "intoxication=100.0 inventoryCleared=true feet=null");
        StringAssert.Contains(output, "[Wrench.Tests.SampleMod] INFO: caught: global 'missingGlobal' is nil");
    }

    [TestMethod]
    public void Exceptions_In_Dotnet_Functions_Become_Lua_Errors()
    {
        var output = RunHello();

        StringAssert.Contains(output, "Fail -> ok=false error=Wrench.Tests.SampleMod: nope");
        StringAssert.Contains(output, "[Wrench.Tests.SampleMod] ERROR: Lua call into a lambda in LuaInteropMod failed");
    }

    [TestMethod]
    public void Mods_Wrap_And_Replace_Existing_Lua_Functions()
    {
        var output = RunHello();

        StringAssert.Contains(output, "createMenu -> [original option, .NET option for player0]");
        StringAssert.Contains(output, "lockDoor -> locked 131,181,0 in .NET");
    }

    [TestMethod]
    public void Patches_Are_Reapplied_After_Lua_Reloads()
    {
        var output = RunHello();

        StringAssert.Contains(output, "createMenu -> [reloaded original, .NET option for player1]");
        Assert.IsGreaterThan(1, Count(output, "[Wrench.Tests.SampleMod] INFO: patched;"));
    }

    [TestMethod]
    public void Script_Callbacks_Resolve_To_Functions_Defined_In_Dotnet()
    {
        var output = RunHello();

        StringAssert.Contains(output, "OnCreate WrenchTest.OnCreate.MakePlate -> true [plated in .NET after 0 hours]");
    }

    [TestMethod]
    public void Dynamic_Calls_Follow_Lua_Colon_Semantics()
    {
        var output = RunHello();

        StringAssert.Contains(output, "patched; steps.get(0)=WelcomeStep same=True items=a,b,c truthy=False/True/False");
    }

    [TestMethod]
    public void Dotnet_Mods_Load_From_Pz_Mod_Folders_Via_Mod_Info()
    {
        var output = RunHello();

        StringAssert.Contains(output, "[Wrench.Host] INFO: Loading .NET mod WrenchTestMod from ");
        StringAssert.Contains(output, "WrenchTestMod/42/dotnet/Wrench.Tests.PzMod.dll");
        StringAssert.Contains(output, "[Wrench.Host] INFO: Loaded WrenchTestMod (Wrench.Tests.PzMod.PzTestMod)");
        Assert.DoesNotContain("Loading .NET mod PlainLuaMod", output);
    }

    [TestMethod]
    public void Disabled_Pz_Mods_Stop_Receiving_Events_And_Patches_Until_Reenabled()
    {
        var output = RunHello();

        // Boots: initial, a reload, disabled, re-enabled. Patches run on three; pings arrive while enabled only.
        Assert.AreEqual(3, Count(output, "[WrenchTestMod] INFO: patch applied"));
        Assert.AreEqual(2, Count(output, "[WrenchTestMod] INFO: ping"));
        StringAssert.Contains(output, "Disabled .NET mod WrenchTestMod");
        StringAssert.Contains(output, "Re-enabled .NET mod WrenchTestMod");
        Assert.AreEqual(1, Count(output, "Loading .NET mod WrenchTestMod"));
    }

    [TestMethod]
    public void Host_Announces_Itself_To_Lua_For_The_Metamod()
    {
        var output = RunHello();

        StringAssert.Contains(output, "WrenchHost.version=0.1.0");
    }

    [TestMethod]
    public void Mods_Breaking_The_Policy_Are_Rejected_Before_Running()
    {
        var output = RunHello();

        StringAssert.Contains(output, "[Wrench.Host] ERROR: Rejected Wrench.Tests.BadMod");
        StringAssert.Contains(output, "Wrench.Tests.BadMod: uses System.IO.File");
        StringAssert.Contains(output, "can declare dotnetPermissions=full in its mod.info");
        Assert.DoesNotContain("bad mod ran", output);
        StringAssert.Contains(output, "[Wrench.Host] INFO: Loaded Wrench.Tests.SampleMod (Wrench.Tests.SampleMod.SampleMod)");
    }

    [TestMethod]
    public void Full_Trust_Mods_Wait_For_The_Player_And_Load_When_Allowed()
    {
        var home = NewHome();
        var first = RunHello(home: home, decision: "allow");

        StringAssert.Contains(first, ".NET mod WrenchFullTrustMod is waiting for the player to approve full");
        StringAssert.Contains(first, "pending: WrenchFullTrustMod [Full access to your computer");
        StringAssert.Contains(first, "lua can decide: false, lua sees pending: false");
        StringAssert.Contains(first, "The player allowed full for .NET mod WrenchFullTrustMod; loading it");
        StringAssert.Contains(first, "[WrenchFullTrustMod] INFO: running with full trust on");
        StringAssert.Contains(first, "[WrenchFullTrustMod] INFO: patch applied");
        Assert.IsTrue(File.Exists(Path.Combine(home, "Wrench", "trust.json")));

        var second = RunHello(home: home);

        StringAssert.Contains(second, "Loading .NET mod WrenchFullTrustMod from");
        StringAssert.Contains(second, "with full (approved earlier)");
        StringAssert.Contains(second, "[WrenchFullTrustMod] INFO: running with full trust on");
        Assert.DoesNotContain("pending: WrenchFullTrustMod", second);
    }

    [TestMethod]
    public void Declined_Full_Trust_Mods_Stay_Unloaded_Without_Asking_Again()
    {
        var home = NewHome();
        var first = RunHello(home: home, decision: "deny");

        StringAssert.Contains(first, "The player declined full for .NET mod WrenchFullTrustMod");
        Assert.DoesNotContain("running with full trust", first);

        var second = RunHello(home: home);

        StringAssert.Contains(second, "Not loading .NET mod WrenchFullTrustMod: the player declined its permissions");
        Assert.DoesNotContain("pending: WrenchFullTrustMod", second);
        Assert.DoesNotContain("running with full trust", second);
    }

    [TestMethod]
    public void Undecided_Full_Trust_Mods_Never_Run()
    {
        var output = RunHello();

        StringAssert.Contains(output, "pending: WrenchFullTrustMod");
        Assert.DoesNotContain("running with full trust", output);
    }

    [TestMethod]
    public void Log_Level_Filters_Host_And_Mod_Output()
    {
        var output = RunHello(new() { ["WRENCH_LOG_LEVEL"] = "Error" });

        StringAssert.Contains(output, "[Wrench.Tests.SampleMod] ERROR: OnGameBoot handler failed");
        Assert.DoesNotContain("INFO:", output);
    }

    // Mirrors the vmArgs in PZ's ProjectZomboid64.json that affect agent loading and signal handling.
    /// <param name="home">The fake PZ user folder holding Wrench/trust.json; fresh per run unless given.</param>
    /// <param name="decision">How the fake player answers the permission prompt: allow, deny or not at all.</param>
    private static string RunHello(Dictionary<string, string>? environment = null, string? home = null, string? decision = null) =>
        RunJava(environment,
            $"-Dwrench.test.home={home ?? NewHome()}",
            decision is null ? "-Dwrench.test.nodecision" : $"-Dwrench.test.decision={decision}",
            "--enable-native-access=ALL-UNNAMED",
            "-XX:+UseZGC",
            $"-javaagent:{Path.Combine(AppContext.BaseDirectory, "dist", "wrench-agent.jar")}",
            $"-Dwrench.test.pzmods={Path.Combine(AppContext.BaseDirectory, "pzmods")}",
            "-cp", Path.Combine(AppContext.BaseDirectory, "classes"),
            "Hello");

    private static string NewHome() =>
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "homes", Guid.NewGuid().ToString("N"))).FullName;

    private static int Count(string text, string value) => text.Split(value).Length - 1;

    private static string RunJava(Dictionary<string, string>? environment, params string[] args)
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        var java = string.IsNullOrEmpty(javaHome) ? "java" : Path.Combine(javaHome, "bin", "java");

        var psi = new ProcessStartInfo(java, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var (name, value) in environment ?? [])
            psi.Environment[name] = value;
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("java did not exit within 60s");
        }

        var output = stdout.Result + stderr.Result;
        Console.WriteLine(output);
        Assert.AreEqual(0, process.ExitCode, output);
        return output;
    }
}
