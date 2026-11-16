using Wrench;

namespace Wrench.Tests.PzMod;

/// <summary>Shipped inside a PZ mod folder; the test's LuaDriver enables and disables it like PZ's mod list would.</summary>
public sealed class PzTestMod : ModInstance
{
    public override void OnLoad()
    {
        PatchLua(() => Log.Info("patch applied"));
        RegisterLuaEvent("OnTestPing", () => Log.Info("ping"));
    }
}
