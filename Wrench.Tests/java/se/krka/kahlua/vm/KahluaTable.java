package se.krka.kahlua.vm;

// Test stand-in for Kahlua's KahluaTable.
public interface KahluaTable {
    void rawset(Object key, Object value);

    Object rawget(Object key);

    KahluaTable getMetatable();

    void setMetatable(KahluaTable metatable);
}
