package io.pznet.wrench;

import java.lang.invoke.MethodHandle;
import java.lang.invoke.MethodHandles;
import java.lang.invoke.MethodType;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.util.concurrent.ConcurrentHashMap;
import se.krka.kahlua.vm.JavaFunction;
import se.krka.kahlua.vm.LuaCallFrame;

/** Runs an unpatched, non-overloaded exposed Java method without pcall, with Kahlua's own converters and errors. */
final class FastJava {
    private record Plan(MethodHandle method, Class<?> owner, Class<?>[] parameters, Object converters, String name,
                        boolean returnsValue) {
    }

    private static final Plan NONE = new Plan(null, null, null, null, null, false);
    private static final ConcurrentHashMap<Object, Plan> PLANS = new ConcurrentHashMap<>();

    private static final Class<?> INVOKER, METHOD_CALLER;
    private static final Field CALLER, MANAGER, NAME, HAS_VARARGS, NEEDS_RETURN_VALUES, CALLER_METHOD;
    private static final MethodHandle FROM_LUA, TO_LUA, LOG_EXCEPTION;

    static {
        Class<?> invoker = null, methodCaller = null;
        Field caller = null, manager = null, name = null, varargs = null, returnValues = null, callerMethod = null;
        MethodHandle fromLua = null, toLua = null, logException = null;
        try {
            invoker = Class.forName("se.krka.kahlua.integration.expose.LuaJavaInvoker");
            methodCaller = Class.forName("se.krka.kahlua.integration.expose.caller.MethodCaller");
            caller = accessible(invoker.getDeclaredField("caller"));
            manager = accessible(invoker.getDeclaredField("manager"));
            name = accessible(invoker.getDeclaredField("name"));
            varargs = accessible(invoker.getDeclaredField("hasVarargs"));
            returnValues = accessible(invoker.getDeclaredField("needsReturnValues"));
            callerMethod = accessible(methodCaller.getDeclaredField("method"));
            var converters = Class.forName("se.krka.kahlua.converter.KahluaConverterManager");
            var lookup = MethodHandles.lookup();
            fromLua = lookup.unreflect(converters.getMethod("fromLuaToJava", Object.class, Class.class));
            toLua = lookup.unreflect(converters.getMethod("fromJavaToLua", Object.class));
            logException = lookup.unreflect(accessible(methodCaller.getDeclaredMethod("logException", Throwable.class)));
        } catch (ReflectiveOperationException | RuntimeException e) {
            // Kahlua's internals are missing or changed (the test fakes, a game update): never take the fast path.
            invoker = null;
        }
        INVOKER = invoker;
        METHOD_CALLER = methodCaller;
        CALLER = caller;
        MANAGER = manager;
        NAME = name;
        HAS_VARARGS = varargs;
        NEEDS_RETURN_VALUES = returnValues;
        CALLER_METHOD = callerMethod;
        FROM_LUA = fromLua;
        TO_LUA = toLua;
        LOG_EXCEPTION = logException;
    }

    static final Object[] NOT_FAST = new Object[0];

    private FastJava() {
    }

    /** {@code function(self, args[argsFrom..])} in pcall's shape, or {@link #NOT_FAST}, having done nothing. */
    static Object[] call(Object function, Object self, Object[] args, int argsFrom) {
        if (INVOKER == null || function == null || function.getClass() != INVOKER)
            return NOT_FAST;
        var plan = PLANS.computeIfAbsent(function, FastJava::plan);
        if (plan == NONE || plan.parameters.length != args.length - argsFrom || !plan.owner.isInstance(self))
            return NOT_FAST;

        var converted = new Object[plan.parameters.length + 1];
        converted[0] = self;
        try {
            for (var i = 0; i < plan.parameters.length; i++) {
                var value = args[argsFrom + i];
                var type = plan.parameters[i];
                var java = value == null ? null : FROM_LUA.invoke(plan.converters, value, type);
                // Kahlua rejects these; let pcall report it as it would.
                if (java == null && (value != null || type.isPrimitive()))
                    return NOT_FAST;
                converted[i + 1] = java;
            }
        } catch (Throwable t) {
            return NOT_FAST;
        }

        Object result;
        try {
            result = plan.method.invokeExact(converted);
        } catch (Throwable t) {
            return failed(plan, t);
        }
        if (!plan.returnsValue)
            return new Object[] {Boolean.TRUE};
        try {
            return new Object[] {Boolean.TRUE, TO_LUA.invoke(plan.converters, result)};
        } catch (Throwable t) {
            return failed(plan, t);
        }
    }

    // Logs and raises the method's exception as MethodCaller and LuaJavaInvoker would, through pcall like the slow path.
    private static Object[] failed(Plan plan, Throwable t) {
        try {
            LOG_EXCEPTION.invoke(t);
        } catch (Throwable ignored) {
            // Logging is best effort; the error itself is what matters.
        }
        var wrapped = new RuntimeException(t);
        JavaFunction rethrow = new JavaFunction() {
            @Override
            public int call(LuaCallFrame frame, int nArguments) {
                throw wrapped;
            }

            @Override
            public String toString() {
                return plan.name;
            }
        };
        return zombie.Lua.LuaManager.thread.pcall(rethrow, new Object[0]);
    }

    private static Plan plan(Object invoker) {
        try {
            var caller = CALLER.get(invoker);
            if (caller == null || caller.getClass() != METHOD_CALLER || HAS_VARARGS.getBoolean(invoker)
                    || NEEDS_RETURN_VALUES.getBoolean(invoker))
                return NONE;
            var method = (Method) CALLER_METHOD.get(caller);
            if (Modifier.isStatic(method.getModifiers()))
                return NONE;
            method.setAccessible(true);
            var parameters = method.getParameterTypes();
            var handle = MethodHandles.lookup().unreflect(method)
                    .asType(MethodType.genericMethodType(parameters.length + 1))
                    .asSpreader(Object[].class, parameters.length + 1);
            return new Plan(handle, method.getDeclaringClass(), parameters, MANAGER.get(invoker), (String) NAME.get(invoker),
                    method.getReturnType() != void.class);
        } catch (ReflectiveOperationException | RuntimeException e) {
            return NONE;
        }
    }

    private static <T extends java.lang.reflect.AccessibleObject> T accessible(T member) {
        member.setAccessible(true);
        return member;
    }
}
