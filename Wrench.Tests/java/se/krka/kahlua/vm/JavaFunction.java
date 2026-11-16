package se.krka.kahlua.vm;

// Test stand-in for Kahlua's JavaFunction (same signature as PZ's).
public interface JavaFunction {
    int call(LuaCallFrame callFrame, int nArguments);
}
