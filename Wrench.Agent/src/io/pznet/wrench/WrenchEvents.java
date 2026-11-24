package io.pznet.wrench;

import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Lua event routing, once before an event's Lua callbacks (Wrench's own hooks) and once after (mod handlers, so they
 * see what Lua mods did). Public because the advice inlined into zombie.Lua.Event calls it from that class.
 */
public final class WrenchEvents {
    private static final Map<String, Integer> IDS = new ConcurrentHashMap<>();
    private static final AtomicInteger NEXT_ID = new AtomicInteger();
    private static final Map<String, Integer> BEFORE = new ConcurrentHashMap<>();
    private static final Map<String, Integer> AFTER = new ConcurrentHashMap<>();

    private WrenchEvents() {
    }

    public static void dispatch(String name, Object[] args, boolean afterLua) {
        // Unsubscribed events never cross into .NET.
        var id = name == null ? null : (afterLua ? AFTER : BEFORE).get(name);
        if (id == null)
            return;
        try {
            // Host hooks don't take arguments.
            WrenchNative.onLuaEvent(id, afterLua ? WrenchLua.encode(args == null ? new Object[0] : args) : null, afterLua);
        } catch (Throwable t) {
            System.err.println("[Wrench] Lua event " + name + " handler failed: " + t);
            t.printStackTrace();
        }
    }

    /** Called from Wrench.Host once its natives are registered; returns the id dispatch passes for the event. */
    public static int subscribe(String name, boolean afterLua) {
        var id = IDS.computeIfAbsent(name, n -> NEXT_ID.getAndIncrement());
        (afterLua ? AFTER : BEFORE).put(name, id);
        return id;
    }

    public static void unsubscribe(String name, boolean afterLua) {
        (afterLua ? AFTER : BEFORE).remove(name);
    }
}
