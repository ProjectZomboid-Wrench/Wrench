package io.pznet.wrench;

import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/** Lua event routing. Public because the advice inlined into zombie.Lua.Event calls it from that class. */
public final class WrenchEvents {
    private static final Set<String> SUBSCRIBED = ConcurrentHashMap.newKeySet();

    private WrenchEvents() {
    }

    public static void dispatch(String name, Object[] args) {
        // Unsubscribed events never cross into .NET.
        if (name == null || !SUBSCRIBED.contains(name))
            return;
        try {
            WrenchNative.onLuaEvent(name, args);
        } catch (Throwable t) {
            System.err.println("[Wrench] Lua event " + name + " handler failed: " + t);
            t.printStackTrace();
        }
    }

    /** Called from Wrench.Host once its natives are registered. */
    public static void subscribe(String name) {
        SUBSCRIBED.add(name);
    }

    public static void unsubscribe(String name) {
        SUBSCRIBED.remove(name);
    }
}
