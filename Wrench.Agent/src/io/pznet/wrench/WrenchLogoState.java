package io.pznet.wrench;

import org.lwjgl.glfw.GLFW;
import org.lwjglx.opengl.Display;
import zombie.core.Core;
import zombie.core.SpriteRenderer;
import zombie.gameStates.GameState;
import zombie.gameStates.GameStateMachine;
import zombie.ui.TextManager;
import zombie.ui.UIFont;
import zombie.ui.UIManager;

/** A short splash after the TIS logo confirming Wrench is running, or saying plainly that its .NET host isn't. */
public final class WrenchLogoState extends GameState {
    private static final double FADE_SECONDS = 0.5;
    private static final double HOLD_SECONDS = 1.5;
    private static final int[] SKIP_KEYS = {32, 257, 256}; // Space, Enter, Escape

    private long startNanos;
    private boolean previousSuspend;

    @Override
    public void enter() {
        startNanos = System.nanoTime();
        previousSuspend = UIManager.suspend;
        UIManager.suspend = true;
    }

    @Override
    public void exit() {
        UIManager.suspend = previousSuspend;
    }

    @Override
    public GameStateMachine.StateAction update() {
        for (var key : SKIP_KEYS) {
            if (GLFW.glfwGetKey(Display.getWindow(), key) == 1)
                return GameStateMachine.StateAction.Continue;
        }
        return elapsed() >= FADE_SECONDS * 2 + HOLD_SECONDS
                ? GameStateMachine.StateAction.Continue
                : GameStateMachine.StateAction.Remain;
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
                0.0F, 0.0F, 0.0F, 1.0F, null);

        var alpha = alpha();
        var text = TextManager.instance;
        var centre = core.getScreenWidth() / 2.0;
        var y = core.getScreenHeight() * 0.4;
        text.DrawStringCentre(UIFont.Title, centre, y, "Wrench", 1.0, 0.8, 0.2, alpha);
        y += text.getFontHeight(UIFont.Title) * 1.5;

        var version = WrenchInfo.hostVersion();
        if (version != null) {
            text.DrawStringCentre(UIFont.Medium, centre, y, "Wrench " + version + " is loaded", 1.0, 1.0, 1.0, alpha);
            y += text.getFontHeight(UIFont.Medium) * 1.4;
            text.DrawStringCentre(UIFont.Small, centre, y,
                    WrenchInfo.runtime() + " on Java " + System.getProperty("java.version"), 0.7, 0.7, 0.7, alpha);
        } else {
            text.DrawStringCentre(UIFont.Medium, centre, y,
                    "The Wrench agent is running, but its .NET host didn't start. .NET mods won't load; see the game log.",
                    1.0, 0.45, 0.4, alpha);
        }

        core.EndFrameUI();
        UIManager.useUiFbo = previousFbo;
    }

    private double elapsed() {
        return (System.nanoTime() - startNanos) / 1e9;
    }

    private double alpha() {
        var t = elapsed();
        if (t < FADE_SECONDS)
            return t / FADE_SECONDS;
        var fadeOutStart = FADE_SECONDS + HOLD_SECONDS;
        return t < fadeOutStart ? 1.0 : Math.max(0.0, 1.0 - (t - fadeOutStart) / FADE_SECONDS);
    }
}
