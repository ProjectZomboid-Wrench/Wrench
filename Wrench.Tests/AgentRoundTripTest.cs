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

        StringAssert.Contains(output, "[Wrench.Host] Attached to");
        StringAssert.Contains(output, "[Wrench.Host] hello from Java");
        StringAssert.Contains(output, "Hello.main ran");
    }

    [TestMethod]
    public void Lua_Event_Trigger_Is_Forwarded_Only_When_Subscribed()
    {
        var output = RunHello();

        StringAssert.Contains(output, "[Wrench.Host] Lua event OnGameBoot(hi, 42, null)");
        StringAssert.Contains(output, "Event.trigger ran: OnGameBoot");
        StringAssert.Contains(output, "Event.trigger ran: OnTick");
        Assert.DoesNotContain("Lua event OnTick", output);
    }

    // Mirrors the vmArgs in PZ's ProjectZomboid64.json that affect agent loading and signal handling.
    private static string RunHello() =>
        RunJava(
            "--enable-native-access=ALL-UNNAMED",
            "-XX:+UseZGC",
            $"-javaagent:{Path.Combine(AppContext.BaseDirectory, "dist", "wrench-agent.jar")}",
            "-cp", Path.Combine(AppContext.BaseDirectory, "classes"),
            "Hello");

    private static string RunJava(params string[] args)
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        var java = string.IsNullOrEmpty(javaHome) ? "java" : Path.Combine(javaHome, "bin", "java");

        var psi = new ProcessStartInfo(java, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
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
