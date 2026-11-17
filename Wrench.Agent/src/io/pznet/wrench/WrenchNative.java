package io.pznet.wrench;

/** Implemented in Wrench.Host; bound with RegisterNatives when the host initialises. */
public final class WrenchNative {
    private WrenchNative() {
    }

    public static native void hello(String message);

    public static native void onLuaEvent(String name, Object[] args, boolean afterLua);

    public static native Object[] invokeFunction(long id, Object[] args);

    public static native void onPermissionDecision(String modId, boolean allow);
}
