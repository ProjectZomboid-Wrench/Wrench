using Zomboid.Java.Characters;
using Zomboid.Java.Inventory;
using Zomboid.Lua.ContextMenus;

namespace Wrench.Tests.SampleMod;

/// <summary>Uses each part of the context-menu API; driven by LuaDriver.inventoryMenu.</summary>
public sealed class MenuMod : ModInstance
{
    public override void OnLoad() => PatchLua(() => Lua.Define("WrenchTest.Taste", (string state) =>
    {
        TasteCommand.State = state;
        TasteCommand.Current?.NotifyCanExecuteChanged();
    }));

    [InventoryCommand("ContextMenu_Serve", Item = "Test.Plate", CanExecute = nameof(CanServe))]
    private void Serve(InventoryItem plate, IsoPlayer player) => plate.Name = $"served {plate.Name} for {player}";

    private static CommandState CanServe(InventoryItem plate, IsoPlayer player) =>
        plate.Name == "cold" ? CommandState.Disabled("Tooltip_Cold") : CommandState.Enabled;

    [InventoryCommand("ContextMenu_Reheat", Item = "Test.Plate", CanExecute = nameof(CanReheat))]
    private static void Reheat(InventoryItem plate) => plate.Name = "reheated";

    private static CommandState CanReheat(InventoryItem plate) =>
        plate.Name == "cold" ? CommandState.Disabled("Tooltip_NoStove") : false;

    [OverrideOption("ContextMenu_Eat")]
    private void Eat(OptionClick click)
    {
        Log.Info($"eat overridden, original target {click.OriginalTarget}");
        click.CallOriginal();
    }
}

internal sealed class StirCommand : InventoryCommand<InventoryItem>
{
    public override string Text => "Stir";

    public override string? Item => "Test.Spoon";

    public override void Execute(InventoryItem spoon, IsoPlayer player) => spoon.Name = "stirred";
}

internal sealed class TasteCommand : InventoryCommand<InventoryItem>
{
    internal static TasteCommand? Current;
    internal static string State = "locked";

    public TasteCommand() => Current = this;

    public override string Text => "Taste";

    public override string? Item => "Test.Spoon";

    public override CommandState CanExecute(InventoryItem spoon, IsoPlayer player) => State switch
    {
        "locked" => CommandState.Disabled("Tooltip_Locked"),
        "open" => CommandState.Enabled,
        _ => CommandState.Hidden,
    };

    public override void Execute(InventoryItem spoon, IsoPlayer player) => spoon.Name = "tasted";
}

internal sealed class GourmetFiller : InventoryMenuFiller
{
    public override void Fill(InventoryMenu menu)
    {
        var submenu = menu.AddSubMenu($"Gourmet ({menu.Items.Count} items)");
        submenu.AddOption("Polish", click => click.Arg<InventoryItem>(0).Name = "polished", menu.Items[1]);
        menu.Find("Stir")!.Tooltip = "Found by the filler";
    }
}
