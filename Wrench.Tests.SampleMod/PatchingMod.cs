using Wrench;
using Zomboid.Java;
using Zomboid.Java.Characters;
using Zomboid.Lua;

namespace Wrench.Tests.SampleMod;

/// <summary>Wraps and replaces existing Lua functions; driven by the test's LuaDriver.</summary>
public sealed class PatchingMod : ModInstance
{
    public override void OnLoad() => PatchLua(Patch);

    private void Patch()
    {
        if (Lua.GetGlobal("ContextMenuBuilder") is null)
            return;

        Lua.Wrap("ContextMenuBuilder.createMenu", call =>
        {
            var options = call.CallOriginal()!;
            options.add($".NET option for {call.Args[0]}");
            return options;
        });
        Lua.Replace("Tutorial1.lockDoor", (int x, int y, int z) => $"locked {x},{y},{z} in .NET");
        Lua.Define("WrenchTest.Typed", (JavaList<string> items) =>
            items.Add($"typed: count={items.Count} first={items[0]} upper={string.Join("|", items.Select(i => i.ToUpperInvariant()))}"));
        Lua.Define("WrenchTest.Inspect", (ISBaseTimedAction action) =>
            $"{(action is ISInventoryTransferAction ? nameof(ISInventoryTransferAction) : nameof(ISBaseTimedAction))} " +
            $"maxTime={action.MaxTime} stopOnWalk={action.StopOnWalk} valid={action.IsValid()}");
        Lua.Define("WrenchTest.Transfer", (IsoGameCharacter character) => new LoggedTransfer(character).Queue());
        Lua.Define("WrenchTest.Sequence", (LuaSequence<string> items) => $"count={items.Count} first={items[0]} last={items[items.Count - 1]}");
        Lua.Define("WrenchTest.Union", (OneOf<string, double, ISBaseTimedAction> value) => value switch
        {
            string text => $"text {text}",
            double number => $"number {number}",
            ISBaseTimedAction action => $"action maxTime={action.MaxTime}",
        });
        Lua.Define("WrenchTest.QueueAction",
            (IsoGameCharacter character, string label) => new TestAction(character, label).Queue());
        Lua.Define("WrenchTest.OnCreate.MakePlate", (dynamic craftData, dynamic character) =>
            craftData.addLog($"plated in .NET after {character.getHoursSurvived()} hours"));

        var steps = Lua.GetGlobal("Steps")!;
        var first = steps.get(0);
        var again = steps.get(0);
        var items = string.Join(",", Lua.GetGlobal("Items")!.Sequence());
        Log.Info($"patched; steps.get(0)={first} same={first == again} items={items} " +
                 $"truthy={Lua.Truthy(false)}/{Lua.Truthy(0)}/{Lua.Truthy(null)}");
    }
}
