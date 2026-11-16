package io.pznet.wrench;

import java.util.ArrayList;
import java.util.List;
import org.lwjgl.glfw.GLFW;
import org.lwjglx.opengl.Display;
import zombie.core.Core;
import zombie.core.SpriteRenderer;
import zombie.gameStates.GameState;
import zombie.gameStates.GameStateMachine;
import zombie.input.GameKeyboard;
import zombie.ui.TextManager;
import zombie.ui.UIFont;
import zombie.ui.UIManager;

/**
 * Asks the player to grant a mod's permissions on a screen of Wrench's own. While it is active PZ's UI (and with it
 * every Lua UI element) is suspended and Lua key events are off, so no Lua code runs; keys are read straight from
 * GLFW, below the lwjglx Keyboard that Lua can inject events into.
 */
public final class WrenchPromptState extends GameState {
    private static final int GLFW_PRESS = 1;
    private static final int KEY_Y = 89;
    private static final int KEY_N = 78;
    private static final int KEY_ESCAPE = 256;

    private WrenchPermissions.Request current;
    private boolean armed;
    private boolean previousSuspend;

    @Override
    public void enter() {
        WrenchPermissions.promptActive = true;
        previousSuspend = UIManager.suspend;
        UIManager.suspend = true;
        GameKeyboard.setDoLuaKeyPressed(false);
        current = WrenchPermissions.next();
        armed = false;
    }

    @Override
    public void exit() {
        UIManager.suspend = previousSuspend;
        GameKeyboard.setDoLuaKeyPressed(true);
        WrenchPermissions.promptActive = false;
    }

    @Override
    public GameStateMachine.StateAction update() {
        if (current == null)
            return GameStateMachine.StateAction.Continue;

        var allow = isDown(KEY_Y);
        var decline = isDown(KEY_N) || isDown(KEY_ESCAPE);
        // Keys already held when the prompt appeared (or from the previous prompt) must be released first.
        if (!armed) {
            armed = !allow && !decline;
            return GameStateMachine.StateAction.Remain;
        }
        if (!allow && !decline)
            return GameStateMachine.StateAction.Remain;

        WrenchPermissions.decide(current.id(), allow && !decline);
        current = WrenchPermissions.next();
        armed = false;
        return current == null ? GameStateMachine.StateAction.Continue : GameStateMachine.StateAction.Remain;
    }

    @Override
    public void render() {
        var core = Core.getInstance();
        core.StartFrame();
        core.EndFrame();
        var previousFbo = UIManager.useUiFbo;
        UIManager.useUiFbo = false;
        core.StartFrameUI();
        SpriteRenderer.instance.renderi(null, 0, 0, core.getOffscreenWidth(0), core.getOffscreenHeight(0),
                0.04F, 0.04F, 0.05F, 1.0F, null);
        if (current != null)
            drawRequest(core.getScreenWidth(), core.getScreenHeight());
        core.EndFrameUI();
        UIManager.useUiFbo = previousFbo;
    }

    private void drawRequest(int width, int height) {
        var text = TextManager.instance;
        var centre = width / 2.0;
        var y = height * 0.22;

        text.DrawStringCentre(UIFont.Medium, centre, y, "Wrench: a mod is asking for permission", 1.0, 0.8, 0.2, 1.0);
        y += text.getFontHeight(UIFont.Medium) * 2.0;
        text.DrawStringCentre(UIFont.Large, centre, y, current.name(), 1.0, 1.0, 1.0, 1.0);
        y += text.getFontHeight(UIFont.Large) * 2.0;

        for (var description : current.descriptions()) {
            for (var line : wrap(description, (int) (width * 0.6))) {
                text.DrawStringCentre(UIFont.Medium, centre, y, line, 0.9, 0.9, 0.9, 1.0);
                y += text.getFontHeight(UIFont.Medium) * 1.2;
            }
            y += text.getFontHeight(UIFont.Medium);
        }

        y += text.getFontHeight(UIFont.Medium);
        text.DrawStringCentre(UIFont.Large, centre, y, "Press Y to allow, or N to decline", 1.0, 1.0, 1.0, 1.0);
        y += text.getFontHeight(UIFont.Large) * 2.0;
        text.DrawStringCentre(UIFont.Small, centre, y,
                "Only Wrench draws this screen, and mods can't answer it for you. You'll be asked again if the mod changes.",
                0.6, 0.6, 0.6, 1.0);
    }

    private static List<String> wrap(String text, int maxWidth) {
        var lines = new ArrayList<String>();
        var line = new StringBuilder();
        for (var word : text.split(" ")) {
            var candidate = line.isEmpty() ? word : line + " " + word;
            if (!line.isEmpty() && TextManager.instance.MeasureStringX(UIFont.Medium, candidate) > maxWidth) {
                lines.add(line.toString());
                line = new StringBuilder(word);
            } else {
                line = new StringBuilder(candidate);
            }
        }
        if (!line.isEmpty())
            lines.add(line.toString());
        return lines;
    }

    private static boolean isDown(int key) {
        return GLFW.glfwGetKey(Display.getWindow(), key) == GLFW_PRESS;
    }
}
