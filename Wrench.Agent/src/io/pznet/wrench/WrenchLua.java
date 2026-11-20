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

    /** Calls a Lua or Java function; reports its results, or throws {@link LuaError} if it raised one. */
    public static Object call(Object function, Object[] args) {
        return encode(pcall(function, args));
    }

    /** {@code target:method(args...)}. */
    public static Object callMethod(Object target, String method, Object[] args) {
        var withSelf = new Object[args.length + 1];
        withSelf[0] = target;
        System.arraycopy(args, 0, withSelf, 1, args.length);
        return invokeMethod(target, method, withSelf);
    }

    // Fixed-arity entry points, so Wrench.Host needn't build an Object[] or box numbers over JNI: bit i of
    // numbers says argument i is the double n<i> rather than the object a<i>.
    public static Object call0(Object function) {
        return call(function, new Object[0]);
    }

    public static Object call1(Object function, int numbers, Object a0, double n0) {
        return call(function, new Object[] {arg(numbers, 0, a0, n0)});
    }

    public static Object call2(Object function, int numbers, Object a0, double n0, Object a1, double n1) {
        return call(function, new Object[] {arg(numbers, 0, a0, n0), arg(numbers, 1, a1, n1)});
    }

    public static Object call3(Object function, int numbers, Object a0, double n0, Object a1, double n1,
                               Object a2, double n2) {
        return call(function, new Object[] {arg(numbers, 0, a0, n0), arg(numbers, 1, a1, n1), arg(numbers, 2, a2, n2)});
    }

    public static Object call4(Object function, int numbers, Object a0, double n0, Object a1, double n1,
                               Object a2, double n2, Object a3, double n3) {
        return call(function, new Object[] {arg(numbers, 0, a0, n0), arg(numbers, 1, a1, n1), arg(numbers, 2, a2, n2),
                arg(numbers, 3, a3, n3)});
    }

    public static Object callMethod0(Object target, String method) {
        return invokeMethod(target, method, new Object[] {target});
    }

    public static Object callMethod1(Object target, String method, int numbers, Object a0, double n0) {
        return invokeMethod(target, method, new Object[] {target, arg(numbers, 0, a0, n0)});
    }

    public static Object callMethod2(Object target, String method, int numbers, Object a0, double n0,
                                     Object a1, double n1) {
        return invokeMethod(target, method, new Object[] {target, arg(numbers, 0, a0, n0), arg(numbers, 1, a1, n1)});
    }

    public static Object callMethod3(Object target, String method, int numbers, Object a0, double n0,
                                     Object a1, double n1, Object a2, double n2) {
        return invokeMethod(target, method, new Object[] {target, arg(numbers, 0, a0, n0), arg(numbers, 1, a1, n1),
                arg(numbers, 2, a2, n2)});
    }

    public static Object callMethod4(Object target, String method, int numbers, Object a0, double n0,
                                     Object a1, double n1, Object a2, double n2, Object a3, double n3) {
        return invokeMethod(target, method, new Object[] {target, arg(numbers, 0, a0, n0), arg(numbers, 1, a1, n1),
                arg(numbers, 2, a2, n2), arg(numbers, 3, a3, n3)});
    }

    public static Object function(long id) {
        return new DotnetFunction(id);
    }

    private static Object arg(int numbers, int index, Object value, double number) {
        return (numbers & (1 << index)) != 0 ? (Object) number : value;
    }

    private static Object invokeMethod(Object target, String method, Object[] withSelf) {
        var function = LuaManager.thread.tableget(target, method);
        if (function == null)
            throw new LuaError("attempted to call method '" + method + "' (a nil value) on " + target);
        return call(function, withSelf);
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
