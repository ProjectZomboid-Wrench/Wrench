using Zomboid.Lua.Options;

namespace Wrench.Tests.SampleMod;

/// <summary>A Mod Options page; driven by LuaDriver.modOptions.</summary>
public sealed class OptionsMod : ModInstance
{
    public override void OnLoad() => PatchLua(() => Lua.Define("WrenchTest.ReadOptions", () =>
        Log.Info($"options: enabled={TestOptions.Enabled.Value} key={TestOptions.Key.Value} speed={TestOptions.Speed.Value} " +
                 $"mode={TestOptions.Mode.Value} name={TestOptions.Nickname.Value}")));
}

internal sealed class TestOptions() : ModOptions("UI_Test_Title")
{
    public static readonly Heading General = new("UI_Test_General");
    public static readonly TickBox Enabled = new("UI_Test_Enabled", true);
    public static readonly KeyBind Key = new("UI_Test_Key", 35);
    public static readonly Slider Speed = new("Speed", 0, 10, 0.5, 2.5);
    public static readonly Separator Line = new();

    public static ComboBox Mode { get; } = new("Mode", ["Off", "On", "Auto"], 2);

    public static TextEntry Nickname { get; } = new("Nickname", "Jane");
}
