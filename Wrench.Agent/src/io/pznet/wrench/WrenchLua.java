package io.pznet.wrench;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.Arrays;
import se.krka.kahlua.vm.KahluaTable;
import zombie.Lua.LuaManager;

/**
 * Lua access for Wrench.Host, going through the same Kahlua paths Lua code uses. Must be called on the thread that
 * owns {@code LuaManager.thread} (the game thread); Kahlua rejects calls from any other.
 */
public final class WrenchLua {
    // Lua values come back through resultBuffer(), as LuaBridge.ReadResults in Wrench.Host describes; keep in sync.
    static final int NIL = 0, DOUBLE = 1, BOOLEAN = 2, INT = 3, LONG = 4, FLOAT = 5, SHORT = 6, BYTE = 7, CHAR = 8,
            STRING = 9, OBJECT = 10;
    static final int MAX_RESULTS = 64;

    private static final ThreadLocal<ByteBuffer> RESULTS = ThreadLocal.withInitial(
            () -> ByteBuffer.allocateDirect(8 + 16 * MAX_RESULTS).order(ByteOrder.nativeOrder()));

    private WrenchLua() {
    }

    public static ByteBuffer resultBuffer() {
        return RESULTS.get();
    }

    public static Object getGlobal(String name) {
        return encode(LuaManager.env.rawget(name));
    }

    public static void setGlobal(String name, Object value) {
        LuaManager.env.rawset(name, value);
    }

    /** {@code target[key]}, honouring metatables, so Java objects resolve methods the way {@code obj:method} does. */
    public static Object index(Object target, Object key) {
        return encode(LuaManager.thread.tableget(target, key));
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

    // How invoke finds the function: target itself, target:name (with self), global table target's name, or global name.
    static final int FUNCTION = 0, METHOD = 1, STATIC = 2, GLOBAL = 3;

    private static final Object[] NO_ARGS = new Object[0];

    /** Calls a function found per {@code mode}; reports its results, or throws {@link LuaError} if it raised one. */
    public static Object invoke(int mode, Object target, String name, Object[] args) {
        var callee = callee(mode, target, name);
        if (mode != METHOD)
            return encode(pcall(callee, args));
        var withSelf = new Object[args.length + 1];
        withSelf[0] = target;
        System.arraycopy(args, 0, withSelf, 1, args.length);
        return encode(pcall(callee, withSelf));
    }

    // Fixed-arity entry points, so Wrench.Host needn't build an Object[] or box numbers over JNI: bit i of
    // numbers says argument i is the double n<i> rather than the object a<i>.
    public static Object invoke0(int mode, Object target, String name) {
        return invoke(mode, target, name, NO_ARGS);
    }

    public static Object invoke1(int mode, Object target, String name, int numbers, Object a0, double n0) {
        var args = args(mode, target, 1);
        var at = args.length - 1;
        args[at] = arg(numbers, 0, a0, n0);
        return encode(pcall(callee(mode, target, name), args));
    }

    public static Object invoke2(int mode, Object target, String name, int numbers, Object a0, double n0,
                                 Object a1, double n1) {
        var args = args(mode, target, 2);
        var at = args.length - 2;
        args[at] = arg(numbers, 0, a0, n0);
        args[at + 1] = arg(numbers, 1, a1, n1);
        return encode(pcall(callee(mode, target, name), args));
    }

    public static Object invoke3(int mode, Object target, String name, int numbers, Object a0, double n0,
                                 Object a1, double n1, Object a2, double n2) {
        var args = args(mode, target, 3);
        var at = args.length - 3;
        args[at] = arg(numbers, 0, a0, n0);
        args[at + 1] = arg(numbers, 1, a1, n1);
        args[at + 2] = arg(numbers, 2, a2, n2);
        return encode(pcall(callee(mode, target, name), args));
    }

    public static Object invoke4(int mode, Object target, String name, int numbers, Object a0, double n0,
                                 Object a1, double n1, Object a2, double n2, Object a3, double n3) {
        var args = args(mode, target, 4);
        var at = args.length - 4;
        args[at] = arg(numbers, 0, a0, n0);
        args[at + 1] = arg(numbers, 1, a1, n1);
        args[at + 2] = arg(numbers, 2, a2, n2);
        args[at + 3] = arg(numbers, 3, a3, n3);
        return encode(pcall(callee(mode, target, name), args));
    }

    /** {@code table.field} on a global table. */
    public static Object getStatic(String table, String field) {
        return encode(LuaManager.thread.tableget(globalTable(table), field));
    }

    public static Object function(long id) {
        return new DotnetFunction(id);
    }

    private static Object arg(int numbers, int index, Object value, double number) {
        return (numbers & (1 << index)) != 0 ? (Object) number : value;
    }

    private static Object[] args(int mode, Object target, int count) {
        if (mode != METHOD)
            return new Object[count];
        var args = new Object[count + 1];
        args[0] = target;
        return args;
    }

    // Error messages match what Wrench.Game.Java reported when it looked these up itself.
    private static Object callee(int mode, Object target, String name) {
        return switch (mode) {
            case FUNCTION -> target;
            case METHOD -> {
                var function = LuaManager.thread.tableget(target, name);
                if (function == null)
                    throw new LuaError("attempted to call method '" + name + "' (a nil value) on " + target);
                yield function;
            }
            case STATIC -> {
                var table = (String) target;
                var function = LuaManager.thread.tableget(globalTable(table), name);
                if (function == null)
                    throw new LuaError(table + "." + name + " is nil");
                yield function;
            }
            case GLOBAL -> {
                var function = LuaManager.env.rawget(name);
                if (function == null)
                    throw new LuaError("global '" + name + "' is nil");
                yield function;
            }
            default -> throw new IllegalArgumentException("invoke mode " + mode);
        };
    }

    private static Object globalTable(String name) {
        var table = LuaManager.env.rawget(name);
        if (kind(table) != OBJECT)
            throw new LuaError("global '" + name + "' is not exposed");
        return table;
    }

    private static Object[] pcall(Object function, Object[] args) {
        if (function == null)
            throw new LuaError("attempted to call a nil value");
        var result = LuaManager.thread.pcall(function, args);
        // Kahlua's pcall returns {true, results...} or {false, message, stacktrace, ...}.
        if (result.length > 0 && Boolean.FALSE.equals(result[0]))
            throw new LuaError(result.length > 1 ? String.valueOf(result[1]) : "unknown Lua error");
        return result.length == 0 ? result : Arrays.copyOfRange(result, 1, result.length);
    }

    private static Object encode(Object value) {
        var buffer = RESULTS.get();
        buffer.putInt(0, 1);
        return put(buffer, 0, value) >= STRING ? value : null;
    }

    private static Object encode(Object[] values) {
        var buffer = RESULTS.get();
        if (values.length > MAX_RESULTS) {
            buffer.putInt(0, -1);
            return values;
        }
        buffer.putInt(0, values.length);
        var objects = 0;
        Object first = null;
        for (var i = 0; i < values.length; i++) {
            if (put(buffer, i, values[i]) >= STRING && objects++ == 0)
                first = values[i];
        }
        if (objects <= 1)
            return first;
        var array = new Object[objects];
        var next = 0;
        for (var value : values) {
            if (kind(value) >= STRING)
                array[next++] = value;
        }
        return array;
    }

    private static int put(ByteBuffer buffer, int index, Object value) {
        var kind = kind(value);
        long bits = switch (kind) {
            case DOUBLE -> Double.doubleToRawLongBits((Double) value);
            case BOOLEAN -> (Boolean) value ? 1 : 0;
            case INT -> (Integer) value;
            case LONG -> (Long) value;
            case FLOAT -> Float.floatToRawIntBits((Float) value);
            case SHORT -> (Short) value;
            case BYTE -> (Byte) value;
            case CHAR -> (Character) value;
            default -> 0;
        };
        var offset = 8 + 16 * index;
        buffer.putInt(offset, kind);
        buffer.putLong(offset + 8, bits);
        return kind;
    }

    private static int kind(Object value) {
        return switch (value) {
            case null -> NIL;
            case Double _ -> DOUBLE;
            case Boolean _ -> BOOLEAN;
            case Integer _ -> INT;
            case Long _ -> LONG;
            case Float _ -> FLOAT;
            case Short _ -> SHORT;
            case Byte _ -> BYTE;
            case Character _ -> CHAR;
            case String _ -> STRING;
            default -> OBJECT;
        };
    }

    public static final class LuaError extends RuntimeException {
        public LuaError(String message) {
            super(message);
        }
    }
}
