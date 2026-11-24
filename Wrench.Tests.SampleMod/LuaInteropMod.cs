using Wrench;

namespace Wrench.Tests.SampleMod;

/// <summary>Registers a challenge the way PZ's Lua challenges do; driven by the test's LuaDriver.</summary>
public sealed class LuaInteropMod : ModInstance
{
    public override void OnLoad() => RegisterLuaEvent(LuaEvents.OnChallengeQuery, AddChallenge);

    private void AddChallenge()
    {
        var challenge = Lua.NewTable();
        challenge["id"] = "WrenchTest";
        challenge["OnInitWorld"] = Lua.Function(OnInitWorld);
        challenge["AddPlayer"] = Lua.Function((double playerNum, GameObject player) => AddPlayer(playerNum, player));
        challenge["Add"] = Lua.Function((int a, int b) => a + b);
        challenge["Fail"] = Lua.Function(() => { throw new InvalidOperationException("nope"); });
        challenge["spawns"] = new[] { new Dictionary<string, object?> { ["x"] = 10788, ["y"] = 9985, ["z"] = 0 } };
        Lua.Call("addChallenge", challenge);
        challenge["name"] = "Wrench test challenge";
    }

    private void OnInitWorld()
    {
        var sandbox = Lua.GetGlobal("SandboxVars")!;
        sandbox.Zombies = 1;
        sandbox.MultiplierConfig = new Dictionary<string, object?>
        {
            ["XPMultiplierGlobal"] = 1,
            ["XPMultiplierGlobalToggle"] = true,
        };
        sandbox.ZombieConfig.PopulationMultiplier = Lua.GetGlobal("ZombiePopulationMultiplier")!.Insane;
    }

    private void AddPlayer(double playerNum, dynamic player)
    {
        if (player.getHoursSurvived() > 0)
            return;
        player.getStats().set("INTOXICATION", 100);
        player.getInventory().clear();
        player.setClothingItem_Feet(null);

        var rooms = Lua.GetGlobal("ArrayList")!.Dot.@new();
        rooms.add("kitchen");
        Log.Info($"AddPlayer({playerNum}) rooms={rooms.size()} player={player}");

        object? mixed = Lua.Call("ResultsMixed");
        object? nilFirst = Lua.Call("ResultsNilFirst");
        object? objects = Lua.Call("ResultsObjects");
        object? many = Lua.Call("ResultsMany");
        object? text = Lua.Call("ResultsText");
        object? seven = Lua.Call("ResultsInt");
        Log.Info($"first results: mixed={mixed} nilFirst={nilFirst ?? "nil"} objects={objects} many={many} text={text} int={(seven is int i ? i : -1)}");

        try
        {
            Lua.Call("missingGlobal");
        }
        catch (LuaException e)
        {
            Log.Info($"caught: {e.Message}");
        }
    }
}
