package io.pznet.wrench;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
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

    // invoke's lookups: target, target:name, globals[target].name (nil allowed if optional), globals[target]:name, globals[name].
    static final int FUNCTION = 0, METHOD = 1, STATIC = 2, GLOBAL = 3, STATIC_METHOD = 4, STATIC_OPTIONAL = 5;

    private static final Object[] NO_ARGS = new Object[0];

    /** Calls a function found per {@code mode}; reports its results, or throws {@link LuaError} if it raised one. */
    public static Object invoke(int mode, Object target, String name, Object[] args) {
        var self = self(mode, target);
        var callee = callee(mode, target, self, name);
        if (callee == null)
            return encode(NO_ARGS);
        if (!hasSelf(mode))
            return encodeResults(pcall(callee, args));
        var withSelf = new Object[args.length + 1];
        withSelf[0] = self;
        System.arraycopy(args, 0, withSelf, 1, args.length);
        return encodeResults(pcall(callee, withSelf));
    }

    // Fixed-arity entry points, so Wrench.Host needn't build an Object[] or box numbers over JNI: bit i of
    // numbers says argument i is the double n<i> rather than the object a<i>.
    public static Object invoke0(int mode, Object target, String name) {
        return invoke(mode, target, name, NO_ARGS);
    }

    public static Object invoke1(int mode, Object target, String name, int numbers, Object a0, double n0) {
        var self = self(mode, target);
        var callee = callee(mode, target, self, name);
        if (callee == null)
            return encode(NO_ARGS);
        var args = args(mode, self, 1);
        var at = args.length - 1;
        args[at] = arg(numbers, 0, a0, n0);
        return encodeResults(pcall(callee, args));
    }

    public static Object invoke2(int mode, Object target, String name, int numbers, Object a0, double n0,
                                 Object a1, double n1) {
        var self = self(mode, target);
        var callee = callee(mode, target, self, name);
        if (callee == null)
            return encode(NO_ARGS);
        var args = args(mode, self, 2);
        var at = args.length - 2;
        args[at] = arg(numbers, 0, a0, n0);
        args[at + 1] = arg(numbers, 1, a1, n1);
        return encodeResults(pcall(callee, args));
    }

    public static Object invoke3(int mode, Object target, String name, int numbers, Object a0, double n0,
                                 Object a1, double n1, Object a2, double n2) {
        var self = self(mode, target);
        var callee = callee(mode, target, self, name);
        if (callee == null)
            return encode(NO_ARGS);
        var args = args(mode, self, 3);
        var at = args.length - 3;
        args[at] = arg(numbers, 0, a0, n0);
        args[at + 1] = arg(numbers, 1, a1, n1);
        args[at + 2] = arg(numbers, 2, a2, n2);
        return encodeResults(pcall(callee, args));
    }

    public static Object invoke4(int mode, Object target, String name, int numbers, Object a0, double n0,
                                 Object a1, double n1, Object a2, double n2, Object a3, double n3) {
        var self = self(mode, target);
        var callee = callee(mode, target, self, name);
        if (callee == null)
            return encode(NO_ARGS);
        var args = args(mode, self, 4);
        var at = args.length - 4;
        args[at] = arg(numbers, 0, a0, n0);
        args[at + 1] = arg(numbers, 1, a1, n1);
        args[at + 2] = arg(numbers, 2, a2, n2);
        args[at + 3] = arg(numbers, 3, a3, n3);
        return encodeResults(pcall(callee, args));
    }

    /** {@code table.field} on a global table. */
    public static Object getStatic(String table, String field) {
        return encode(LuaManager.thread.tableget(globalTable(table), field));
    }

    /** Argument {@code index} of each event in a batch; nil where it's missing or not a game object, so the host needn't check. */
    public static Object[] column(Object[] events, int index) {
        var column = new Object[events.length];
        for (var i = 0; i < events.length; i++) {
            var args = (Object[]) events[i];
            var value = index < args.length ? args[index] : null;
            column[i] = kind(value) == OBJECT ? value : null;
        }
        return column;
    }

    /** The targets for which {@code target:method(args...)} returns a truthy value, in order, as Lua would see them. */
    public static Object[] filter(Object[] targets, String method, Object[] args) {
        var kept = new ArrayList<Object>(targets.length);
        var withSelf = new Object[args.length + 1];
        for (var target : targets) {
            if (target == null)
                continue;
            withSelf[0] = target;
            System.arraycopy(args, 0, withSelf, 1, args.length);
            var function = LuaManager.thread.tableget(target, method);
            if (function == null)
                throw new LuaError("attempted to call method '" + method + "' (a nil value) on " + target);
            // pcall writes its results into the array it's given, so each call gets the arguments afresh.
            var result = pcall(function, withSelf);
            if (result.length > 1 && result[1] != null && !Boolean.FALSE.equals(result[1]))
                kept.add(target);
        }
        return kept.toArray();
    }

    public static Object function(long id) {
        return new DotnetFunction(id);
    }

    private static Object arg(int numbers, int index, Object value, double number) {
        return (numbers & (1 << index)) != 0 ? (Object) number : value;
    }

    private static boolean hasSelf(int mode) {
        return mode == METHOD || mode == STATIC_METHOD;
    }

    private static Object self(int mode, Object target) {
        return switch (mode) {
            case METHOD -> target;
            case STATIC_METHOD -> globalTable((String) target);
            default -> null;
        };
    }

    private static Object[] args(int mode, Object self, int count) {
        if (!hasSelf(mode))
            return new Object[count];
        var args = new Object[count + 1];
        args[0] = self;
        return args;
    }

    // Errors match what Wrench.Game.Java raised when it did these lookups; null means an optional static is missing.
    private static Object callee(int mode, Object target, Object self, String name) {
        return switch (mode) {
            case FUNCTION -> target;
            case METHOD, STATIC_METHOD -> {
                var function = LuaManager.thread.tableget(self, name);
                if (function == null)
                    throw new LuaError("attempted to call method '" + name + "' (a nil value) on " + self);
                yield function;
            }
            case STATIC, STATIC_OPTIONAL -> {
                var table = (String) target;
                var function = LuaManager.thread.tableget(globalTable(table), name);
                if (function == null && mode == STATIC)
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

    // Kahlua's pcall returns {true, results...} or {false, message, stacktrace, ...}; returned as is, status first.
    private static Object[] pcall(Object function, Object[] args) {
        if (function == null)
            throw new LuaError("attempted to call a nil value");
        var result = args.length > 0 ? FastJava.call(function, args[0], args, 1) : FastJava.NOT_FAST;
        if (result == FastJava.NOT_FAST)
            result = LuaManager.thread.pcall(function, args);
        if (result.length > 0 && Boolean.FALSE.equals(result[0]))
            throw new LuaError(result.length > 1 ? String.valueOf(result[1]) : "unknown Lua error");
        return result;
    }

    private static Object encodeResults(Object[] pcallResult) {
        return pcallResult.length == 0 ? encode(pcallResult) : encode(pcallResult, 1);
    }

    private static Object encode(Object value) {
        var buffer = RESULTS.get();
        buffer.putInt(0, 1);
        return put(buffer, 0, value) >= STRING ? value : null;
    }

    static Object encode(Object[] values) {
        return encode(values, 0);
    }

    /** Encodes {@code values[from..]}, so callers needn't copy an array to drop a prefix. */
    static Object encode(Object[] values, int from) {
        var buffer = RESULTS.get();
        var count = values.length - from;
        if (count > MAX_RESULTS) {
            buffer.putInt(0, -1);
            return from == 0 ? values : Arrays.copyOfRange(values, from, values.length);
        }
        buffer.putInt(0, count);
        var objects = 0;
        Object first = null;
        for (var i = 0; i < count; i++) {
            if (put(buffer, i, values[from + i]) >= STRING && objects++ == 0)
                first = values[from + i];
        }
        if (objects <= 1)
            return first;
        var array = new Object[objects];
        var next = 0;
        for (var i = from; i < values.length; i++) {
            if (kind(values[i]) >= STRING)
                array[next++] = values[i];
        }
        return array;
    }

    /** Reads values the host wrote the same way, for its .NET functions' results. */
    static Object[] decode(Object objects) {
        var buffer = RESULTS.get();
        var count = buffer.getInt(0);
        if (count < 0)
            return (Object[]) objects;
        var references = 0;
        for (var i = 0; i < count; i++) {
            if (buffer.getInt(8 + 16 * i) >= STRING)
                references++;
        }
        var values = new Object[count];
        var next = 0;
        for (var i = 0; i < count; i++) {
            var kind = buffer.getInt(8 + 16 * i);
            var bits = buffer.getLong(16 + 16 * i);
            values[i] = switch (kind) {
                case NIL -> null;
                case DOUBLE -> Double.longBitsToDouble(bits);
                case BOOLEAN -> bits != 0;
                case INT -> (int) bits;
                case LONG -> bits;
                case FLOAT -> Float.intBitsToFloat((int) bits);
                case SHORT -> (short) bits;
                case BYTE -> (byte) bits;
                case CHAR -> (char) bits;
                default -> references == 1 ? objects : ((Object[]) objects)[next++];
            };
        }
        return values;
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
