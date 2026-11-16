using Wrench;

namespace Wrench.Tests.FullTrustMod;

/// <summary>Reads Environment, which vetting rejects; it only loads once the player grants full trust.</summary>
public sealed class FullTrustMod : ModInstance
{
    public override void OnLoad()
    {
        Log.Info($"running with full trust on {Environment.ProcessorCount} cpus");
        PatchLua(() => Log.Info("patch applied"));
    }
}
