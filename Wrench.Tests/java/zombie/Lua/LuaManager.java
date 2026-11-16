package zombie.Lua;

import se.krka.kahlua.j2se.J2SEPlatform;
import se.krka.kahlua.vm.KahluaTable;
import se.krka.kahlua.vm.KahluaThread;

// Test stand-in for PZ's LuaManager; LuaDriver fills these in.
public final class LuaManager {
    public static J2SEPlatform platform;
    public static KahluaTable env;
    public static KahluaThread thread;
}
