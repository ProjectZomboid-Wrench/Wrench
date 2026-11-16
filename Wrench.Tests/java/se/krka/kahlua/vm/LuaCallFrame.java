package se.krka.kahlua.vm;

import java.util.ArrayList;
import java.util.List;

// Test stand-in for Kahlua's LuaCallFrame: arguments in, pushed results out.
public final class LuaCallFrame {
    private final Object[] args;
    final List<Object> results = new ArrayList<>();

    LuaCallFrame(Object[] args) {
        this.args = args;
    }

    public Object get(int index) {
        return index < args.length ? args[index] : null;
    }

    public int push(Object value) {
        results.add(value);
        return 1;
    }
}
