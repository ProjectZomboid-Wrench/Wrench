package se.krka.kahlua.vm;

import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.util.Arrays;

// Test stand-in for Kahlua's KahluaThread. Indexing a Java object yields a method binding, mimicking PZ's exposer:
// obj:method(...) calls the public method by name and arity, converting Lua numbers to the parameter types.
public class KahluaThread {
    public Object tableget(Object table, Object key) {
        if (table instanceof KahluaTable t)
            return t.rawget(key);
        if (table == null)
            throw new RuntimeException("attempted to index a nil value");
        return new MethodBinding(String.valueOf(key));
    }

    public void tableSet(Object table, Object key, Object value) {
        if (!(table instanceof KahluaTable t))
            throw new RuntimeException("attempted to index a non-table value");
        t.rawset(key, value);
    }

    public Object[] pcall(Object function, Object[] args) {
        if (!(function instanceof JavaFunction f))
            return new Object[] {Boolean.FALSE, "attempt to call a non-function value"};
        try {
            var frame = new LuaCallFrame(args);
            f.call(frame, args.length);
            var result = new Object[frame.results.size() + 1];
            result[0] = Boolean.TRUE;
            for (var i = 0; i < frame.results.size(); i++)
                result[i + 1] = frame.results.get(i);
            return result;
        } catch (RuntimeException e) {
            return new Object[] {Boolean.FALSE, e.getMessage()};
        }
    }

    private record MethodBinding(String name) implements JavaFunction {
        @Override
        public int call(LuaCallFrame frame, int nArguments) {
            var self = frame.get(0);
            var args = new Object[nArguments - 1];
            for (var i = 1; i < nArguments; i++)
                args[i - 1] = frame.get(i);
            var method = Arrays.stream(self.getClass().getMethods())
                    .filter(m -> m.getName().equals(name) && m.getParameterCount() == args.length)
                    .findFirst()
                    .orElseThrow(() -> new RuntimeException("no method " + name + "/" + args.length + " on " + self));
            try {
                var result = method.invoke(self, convert(method, args));
                if (method.getReturnType() == void.class)
                    return 0;
                frame.push(result instanceof Number n && !(result instanceof Double) ? (Object) n.doubleValue() : result);
                return 1;
            } catch (IllegalAccessException | InvocationTargetException e) {
                throw new RuntimeException(e);
            }
        }

        private static Object[] convert(Method method, Object[] args) {
            var types = method.getParameterTypes();
            var converted = new Object[args.length];
            for (var i = 0; i < args.length; i++) {
                if (args[i] instanceof Double d) {
                    if (types[i] == int.class || types[i] == Integer.class) converted[i] = d.intValue();
                    else if (types[i] == float.class || types[i] == Float.class) converted[i] = d.floatValue();
                    else if (types[i] == long.class || types[i] == Long.class) converted[i] = d.longValue();
                    else converted[i] = d;
                } else {
                    converted[i] = args[i];
                }
            }
            return converted;
        }
    }
}
