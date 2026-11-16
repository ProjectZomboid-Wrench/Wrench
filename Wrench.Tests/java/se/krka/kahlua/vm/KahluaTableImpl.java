package se.krka.kahlua.vm;

import java.util.LinkedHashMap;
import java.util.Map;

public final class KahluaTableImpl implements KahluaTable {
    private final Map<Object, Object> entries = new LinkedHashMap<>();

    @Override
    public void rawset(Object key, Object value) {
        if (value == null)
            entries.remove(key);
        else
            entries.put(key, value);
    }

    @Override
    public Object rawget(Object key) {
        return entries.get(key);
    }

    @Override
    public String toString() {
        return "table" + entries.keySet();
    }
}
