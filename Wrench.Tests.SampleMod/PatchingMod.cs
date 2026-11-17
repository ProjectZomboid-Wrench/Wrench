using Wrench;
using Zomboid;
using Zomboid.Characters;

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
