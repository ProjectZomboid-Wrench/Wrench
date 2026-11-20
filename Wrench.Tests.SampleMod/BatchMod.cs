using Wrench;

namespace Wrench.Tests.SampleMod;

/// <summary>Receives LoadGridsquare in batches and filters them in Java; driven by the test's LuaDriver.</summary>
public sealed class BatchMod : ModInstance
{
    public override void OnLoad() => RegisterLuaEventBatch(LuaEvents.LoadGridsquare, OnSquares);

    private void OnSquares(LuaEventBatch batch)
    {
        var names = new List<string>();
        foreach (var square in batch.Argument(0).Where("isSolidTrans"))
            names.Add(square?.ToString() ?? "nil");
        Log.Info($"LoadGridsquare batch of {batch.Count}: solid={string.Join(",", names)}");
    }
}
