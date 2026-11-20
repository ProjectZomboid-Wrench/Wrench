using Zomboid.Lua.Options;

namespace Wrench.Tests.SampleMod;

/// <summary>A Mod Options page; driven by LuaDriver.modOptions.</summary>
public sealed class OptionsMod : ModInstance
{
    public override void OnLoad()
    {
        TestOptions.Nickname.IsEnabled = false;
        TestOptions.Flags.SetEnabled(1, false);
        TestOptions.Mode.AddItem("Later");
        TestOptions.Enabled.Changed += value => Log.Info($"Enabled changing to {value}");
        TestOptions.Enabled.Applied += value => Log.Info($"Enabled applied {value}");
        TestOptions.Tint.Changed += color => Log.Info($"Tint changing to {color}");
        TestOptions.Flags.Applied += (index, ticked) => Log.Info($"Flags[{index}] applied {ticked}");
        TestOptions.Applying = () => Log.Info("options applied");
        TestOptions.ResetClicked = () =>
        {
            TestOptions.Speed.Value = 1;
            TestOptions.Mode.AddItem("Clicked");
            Log.Info("reset clicked");
        };
        PatchLua(() => Lua.Define("WrenchTest.ReadOptions", () =>
            Log.Info($"options: enabled={TestOptions.Enabled.Value} key={TestOptions.Key.Value} speed={TestOptions.Speed.Value} " +
                     $"mode={TestOptions.Mode.Value} name={TestOptions.Nickname.Value} tint={TestOptions.Tint.Value} " +
                     $"flags={TestOptions.Flags[0]},{TestOptions.Flags[1]} item={TestOptions.Mode.SelectedItem}")));
    }
}

internal sealed class TestOptions() : ModOptions("UI_Test_Title")
{
    public static Action? Applying;
    public static Action? ResetClicked;

    public static readonly Heading General = new("UI_Test_General");
    public static readonly TickBox Enabled = new("UI_Test_Enabled", true);
    public static readonly KeyBind Key = new("UI_Test_Key", 35);
    public static readonly Slider Speed = new("Speed", 0, 10, 0.5, 2.5);
    public static readonly Separator Line = new();

    public static ComboBox Mode { get; } = new("Mode", ["Off", "On", "Auto"], 2);

    public static TextEntry Nickname { get; } = new("Nickname", "Jane");

    public static readonly ColorPicker Tint = new("Tint", new OptionColor(0, 0.5, 1));
    public static readonly MultipleTickBox Flags = new("Flags", [("North", true), ("South", false)]);
    public static readonly Button Reset = new("Reset", () => ResetClicked?.Invoke());

    protected override void OnApply() => Applying?.Invoke();
}
