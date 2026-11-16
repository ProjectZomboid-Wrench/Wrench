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
    public void Log_Level_Filters_Host_And_Mod_Output()
    {
        var output = RunHello(new() { ["WRENCH_LOG_LEVEL"] = "Error" });

        StringAssert.Contains(output, "[Wrench.Tests.SampleMod] ERROR: OnGameBoot handler failed");
        Assert.DoesNotContain("INFO:", output);
    }

    // Mirrors the vmArgs in PZ's ProjectZomboid64.json that affect agent loading and signal handling.
    private static string RunHello(Dictionary<string, string>? environment = null) =>
        RunJava(environment,
            "--enable-native-access=ALL-UNNAMED",
            "-XX:+UseZGC",
            $"-javaagent:{Path.Combine(AppContext.BaseDirectory, "dist", "wrench-agent.jar")}",
            "-cp", Path.Combine(AppContext.BaseDirectory, "classes"),
            "Hello");

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
