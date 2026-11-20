package io.pznet.wrench;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Lua event routing, once before an event's Lua callbacks (Wrench's own hooks) and once after (mod handlers, so they
 * see what Lua mods did). Public because the advice inlined into zombie.Lua.Event and batch boundaries calls it.
 */
public final class WrenchEvents {
    private static final Map<String, Integer> IDS = new ConcurrentHashMap<>();
    private static final AtomicInteger NEXT_ID = new AtomicInteger();
    private static final Map<String, Integer> BEFORE = new ConcurrentHashMap<>();
    private static final Map<String, Integer> AFTER = new ConcurrentHashMap<>();
    private static final Map<String, Integer> BATCHED = new ConcurrentHashMap<>();
    private static final ThreadLocal<Batch> BATCH = ThreadLocal.withInitial(Batch::new);
    private static final Map<String, java.util.Set<String>> FLUSHED_AT = new ConcurrentHashMap<>();

    /** The events collected on this thread while inside batch boundary methods. */
    private static final class Batch {
        int depth;
        final Map<Integer, ArrayList<Object[]>> pending = new LinkedHashMap<>();
    }

    private WrenchEvents() {
    }

    public static void dispatch(String name, Object[] args, boolean afterLua) {
        if (name == null)
            return;
        if (afterLua && !BATCHED.isEmpty()) {
            var batched = BATCHED.get(name);
            if (batched != null)
                collect(batched, args);
        }
        // Unsubscribed events never cross into .NET.
        var id = (afterLua ? AFTER : BEFORE).get(name);
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

    public static void openBatch() {
        BATCH.get().depth++;
    }

    public static void closeBatch() {
        var batch = BATCH.get();
        if (--batch.depth > 0)
            return;
        for (var id : batch.pending.keySet())
            deliverPending(batch, id);
    }

    /** At a flush point ("class#method"): delivers the batches of the events that flush there, if any are pending. */
    public static void flushBatch(String method) {
        var batch = BATCH.get();
        if (batch.depth == 0 || batch.pending.isEmpty())
            return;
        for (var name : FLUSHED_AT.computeIfAbsent(method, EventBatches::eventsFlushedAt)) {
            var id = BATCHED.get(name);
            if (id != null)
                deliverPending(batch, id);
        }
    }

    private static void deliverPending(Batch batch, int id) {
        var events = batch.pending.get(id);
        if (events == null || events.isEmpty())
            return;
        var array = events.toArray(new Object[0]);
        events.clear();
        deliver(id, array);
    }

    // The game may reuse an event's argument array, so a batch keeps its own copy.
    private static void collect(int id, Object[] args) {
        var copy = args == null ? new Object[0] : args.clone();
        var batch = BATCH.get();
        if (batch.depth > 0)
            batch.pending.computeIfAbsent(id, k -> new ArrayList<>()).add(copy);
        else
            deliver(id, new Object[] {copy});
    }

    private static void deliver(int id, Object[] events) {
        try {
            WrenchNative.onLuaEventBatch(id, events);
        } catch (Throwable t) {
            System.err.println("[Wrench] Lua event batch handler failed: " + t);
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

    /** Batched delivery, after the event's Lua callbacks; returns the id the batch native passes. */
    public static int subscribeBatch(String name) {
        var id = IDS.computeIfAbsent(name, n -> NEXT_ID.getAndIncrement());
        BATCHED.put(name, id);
        return id;
    }

    public static void unsubscribeBatch(String name) {
        BATCHED.remove(name);
    }
}
