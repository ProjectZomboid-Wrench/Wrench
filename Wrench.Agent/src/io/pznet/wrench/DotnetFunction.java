package io.pznet.wrench;

import se.krka.kahlua.vm.JavaFunction;
import se.krka.kahlua.vm.LuaCallFrame;

/** A Lua-callable function backed by a delegate registered in Wrench.Host under {@code id}. */
public final class DotnetFunction implements JavaFunction {
    private final long id;

    DotnetFunction(long id) {
        this.id = id;
    }

    @Override
    public int call(LuaCallFrame callFrame, int nArguments) {
        var args = new Object[nArguments];
        for (var i = 0; i < nArguments; i++)
            args[i] = callFrame.get(i);
        var results = WrenchNative.invokeFunction(id, args);
        if (results == null)
            return 0;
        for (var result : results)
            callFrame.push(result);
        return results.length;
    }

    @Override
    public String toString() {
        return "dotnet function #" + id;
    }
}
