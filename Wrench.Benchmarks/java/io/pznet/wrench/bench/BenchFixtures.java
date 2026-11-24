package io.pznet.wrench.bench;

import io.pznet.wrench.WrenchEvents;
import se.krka.kahlua.vm.JavaFunction;
import se.krka.kahlua.vm.KahluaThread;
import zombie.Lua.KahluaArrayConverter;
import zombie.Lua.KahluaNumberConverter;
import zombie.Lua.LuaManager;
import zombie.core.Color;

import java.util.ArrayList;

/** PZ's real Kahlua set up without the game, plus Java-side loops that drive calls into .NET. */
public final class BenchFixtures {
    private BenchFixtures() {
    }

    public static void setup() {
        var platform = LuaManager.platform;
        var converters = LuaManager.converterManager;
        // Not newEnvironment(): that loads stdlib.lua from the working directory.
        LuaManager.env = platform.newTable();
        LuaManager.thread = new KahluaThread(platform, LuaManager.env);
        KahluaNumberConverter.install(converters);
        new KahluaArrayConverter(platform, converters).install();
        var exposer = new LuaManager.Exposer(converters, platform, LuaManager.env);
        LuaManager.exposer = exposer;
        for (var cls : new Class<?>[] {Color.class, ArrayList.class}) {
            exposer.setExposed(cls);
            exposer.exposeLikeJava(cls, LuaManager.env);
        }
        LuaManager.env.rawset("benchGlobal", (JavaFunction) (frame, n) -> frame.push(1.0));
    }

    // Kahlua dereferences its owner thread, and BenchmarkDotNet's in-process runs use a new thread per benchmark.
    public static void claimThread() {
        LuaManager.thread.debugOwnerThread = Thread.currentThread();
    }

    public static Object color() {
        return new Color(0.5f, 0.25f, 0.125f, 1f);
    }

    public static int nop() {
        return 0;
    }

    public static void callGlobal(String name, Object arg, int times) {
        var function = LuaManager.env.rawget(name);
        for (var i = 0; i < times; i++) {
            // A new array each call: pcall writes its results into the one it's given.
            var result = LuaManager.thread.pcall(function, new Object[] {arg});
            if (result.length > 0 && Boolean.FALSE.equals(result[0]))
                throw new IllegalStateException(name + " failed: " + (result.length > 1 ? result[1] : "?"));
        }
    }

    public static void fireEvent(String name, Object arg, int times) {
        var args = new Object[] {arg};
        for (var i = 0; i < times; i++)
            WrenchEvents.dispatch(name, args, true);
    }
}
