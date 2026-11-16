package io.pznet.wrench;

import java.util.ArrayList;
import java.util.List;

/**
 * Mods waiting for the player to grant their permissions. Wrench.Host fills this; only {@link WrenchPromptState},
 * a game state Lua can't see into, answers it. Lua has no way to call {@link #decide}.
 */
public final class WrenchPermissions {
    public record Request(String id, String name, String[] descriptions) {
    }

    private static final List<Request> PENDING = new ArrayList<>();
    private static boolean yieldRequested;
    static boolean promptActive;

    private WrenchPermissions() {
    }

    /** Called by Wrench.Host on every Lua (re)load, before re-adding whatever is still undecided. */
    public static synchronized void clear() {
        PENDING.clear();
    }

    public static synchronized void request(String id, String name, String[] descriptions) {
        PENDING.add(new Request(id, name, descriptions));
    }

    public static synchronized List<Request> pending() {
        return List.copyOf(PENDING);
    }

    static synchronized Request next() {
        return PENDING.isEmpty() ? null : PENDING.get(0);
    }

    /** Records the player's answer in Wrench.Host, which loads the mod straight away if allowed. */
    static void decide(String id, boolean allow) {
        synchronized (WrenchPermissions.class) {
            PENDING.removeIf(r -> r.id().equals(id));
        }
        WrenchNative.onPermissionDecision(id, allow);
    }

    // The main menu yields to the prompt only from a plain "stay here" update, so starting a game is never hijacked;
    // redirectState then has to see the request from that same update.
    public static synchronized boolean shouldYield() {
        yieldRequested = !promptActive && !PENDING.isEmpty();
        return yieldRequested;
    }

    public static synchronized Object takeYieldRedirect(Object state) {
        if (!yieldRequested || !state.getClass().getName().equals("zombie.gameStates.MainScreenState"))
            return null;
        yieldRequested = false;
        return new WrenchPromptState();
    }
}
