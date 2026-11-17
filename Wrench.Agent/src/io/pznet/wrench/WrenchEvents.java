package io.pznet.wrench;

import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Lua event routing, once before an event's Lua callbacks (Wrench's own hooks) and once after (mod handlers, so they
 * see what Lua mods did). Public because the advice inlined into zombie.Lua.Event calls it from that class.
 */
public final class WrenchEvents {
    private static final Set<String> BEFORE = ConcurrentHashMap.newKeySet();
    private static final Set<String> AFTER = ConcurrentHashMap.newKeySet();

    private WrenchEvents() {
    }

    public static void dispatch(String name, Object[] args, boolean afterLua) {
        // Unsubscribed events never cross into .NET.
        if (name == null || !(afterLua ? AFTER : BEFORE).contains(name))
            return;
        try {
            WrenchNative.onLuaEvent(name, args, afterLua);
        } catch (Throwable t) {
            System.err.println("[Wrench] Lua event " + name + " handler failed: " + t);
            t.printStackTrace();
        }
    }

    /** Called from Wrench.Host once its natives are registered. */
    public static void subscribe(String name, boolean afterLua) {
        (afterLua ? AFTER : BEFORE).add(name);
    }

    public static void unsubscribe(String name, boolean afterLua) {
        (afterLua ? AFTER : BEFORE).remove(name);
    }
}
