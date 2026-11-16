using Wrench;

namespace Wrench.Tests.BadMod;

/// <summary>Writes outside the game's reach on load; Wrench must refuse to load it at all.</summary>
public sealed class BadMod : ModInstance
{
    public override void OnLoad()
    {
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "wrench-badmod-ran"), "should never exist");
        Log.Info("bad mod ran");
    }
}
