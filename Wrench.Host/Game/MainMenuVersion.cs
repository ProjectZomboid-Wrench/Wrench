using Wrench.Host.Logging;
using Wrench.Host.Lua;

namespace Wrench.Host.Game;

/// <summary>Shows Wrench's version above PZ's own on the main menu.</summary>
internal static class MainMenuVersion
{
    private const string LabelField = "wrenchVersionLabel";

    private static readonly Logger Log = Logging.Log.For("Wrench.Host");
    private static GameObject? _original;

    /// <summary>
    /// Runs on every OnGameBoot. The menu is built by a Lua OnMainMenuEnter callback, after any host hook could add to
    /// it, so this wraps the Lua function that builds it instead.
    /// </summary>
    public static void Install(string text)
    {
        var lua = LuaBridge.Instance;
        if (lua.GetGlobal("LoadMainScreenPanelInt") is not GameObject builder)
            return;
        _original?.Dispose();
        var original = _original = builder.Keep();
        LuaBridge.Instance.SetHostGlobal("LoadMainScreenPanelInt", (Action<object?[]>)(args =>
        {
            lua.Call(null, original, args);
            try
            {
                AddLabel(lua, text);
            }
            catch (Exception e)
            {
                Log.Warn($"Couldn't add the version to the main menu: {e.Message}");
            }
        }));
    }

    private static void AddLabel(ILuaHost lua, string text)
    {
        dynamic mainScreen = lua.GetGlobal("MainScreen")!;
        dynamic screen = mainScreen.instance;
        if (screen is null || screen[LabelField] is not null)
            return;

        dynamic font = ((dynamic)lua.GetGlobal("UIFont")!).Small;
        dynamic textManager = lua.Call(null, lua.GetGlobal("getTextManager")!, [])[0]!;
        double width = textManager.MeasureStringX(font, text);
        double height = textManager.getFontHeight(font);
        dynamic version = screen.versionBtn;
        double y = version is null ? (double)screen.height - height * 2 - 5 : (double)version.y - height;
        double x = (double)screen.width / 2 - width / 2;

        dynamic labelClass = lua.GetGlobal("ISLabel")!;
        dynamic label = labelClass.@new(x, y, height, text, 1, 1, 1, 0.7, font, true);
        label.initialise();
        label.setAnchorTop(false);
        label.setAnchorBottom(true);
        screen.addChild(label);
        screen[LabelField] = label;
    }
}
