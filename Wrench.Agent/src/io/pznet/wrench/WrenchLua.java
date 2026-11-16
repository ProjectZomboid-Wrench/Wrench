package io.pznet.wrench;

import java.util.Arrays;
import se.krka.kahlua.vm.KahluaTable;
import zombie.Lua.LuaManager;

/**
 * Lua access for Wrench.Host, going through the same Kahlua paths Lua code uses. Must be called on the thread that
 * owns {@code LuaManager.thread} (the game thread); Kahlua rejects calls from any other.
 */
public final class WrenchLua {
    private WrenchLua() {
    }

    public static Object getGlobal(String name) {
        return LuaManager.env.rawget(name);
    }

    public static void setGlobal(String name, Object value) {
        LuaManager.env.rawset(name, value);
    }

    /** {@code target[key]}, honouring metatables, so Java objects resolve methods the way {@code obj:method} does. */
    public static Object index(Object target, Object key) {
        return LuaManager.thread.tableget(target, key);
    }

    public static void setIndex(Object target, Object key, Object value) {
        LuaManager.thread.tableSet(target, key, value);
    }

    public static boolean isTable(Object value) {
        return value instanceof KahluaTable;
    }

    public static Object newTable() {
        return LuaManager.platform.newTable();
    }

    /** Calls a Lua or Java function; returns its results, or throws {@link LuaError} if it raised one. */
    public static Object[] call(Object function, Object[] args) {
        if (function == null)
            throw new LuaError("attempted to call a nil value");
        var result = LuaManager.thread.pcall(function, args);
        // Kahlua's pcall returns {true, results...} or {false, message, stacktrace, ...}.
        if (result.length > 0 && Boolean.FALSE.equals(result[0]))
            throw new LuaError(result.length > 1 ? String.valueOf(result[1]) : "unknown Lua error");
        return result.length == 0 ? result : Arrays.copyOfRange(result, 1, result.length);
    }

    /** {@code target:method(args...)}. */
    public static Object[] callMethod(Object target, String method, Object[] args) {
        var function = index(target, method);
        if (function == null)
            throw new LuaError("attempted to call method '" + method + "' (a nil value) on " + target);
        var withSelf = new Object[args.length + 1];
        withSelf[0] = target;
        System.arraycopy(args, 0, withSelf, 1, args.length);
        return call(function, withSelf);
    }

    public static Object function(long id) {
        return new DotnetFunction(id);
    }

    public static final class LuaError extends RuntimeException {
        public LuaError(String message) {
            super(message);
        }
    }
}
