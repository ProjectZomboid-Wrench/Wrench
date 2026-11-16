package io.pznet.wrench;

/** Implemented in Wrench.Host; bound with RegisterNatives when the host initialises. */
public final class WrenchNative {
    private WrenchNative() {
    }

    public static native void hello(String message);

    public static native void onLuaEvent(String name, Object[] args);
}
