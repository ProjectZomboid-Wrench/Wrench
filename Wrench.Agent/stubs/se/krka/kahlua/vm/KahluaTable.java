package se.krka.kahlua.vm;

public interface KahluaTable {
    void rawset(Object key, Object value);

    Object rawget(Object key);
}
