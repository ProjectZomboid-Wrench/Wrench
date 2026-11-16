package io.pznet.wrench;

/** What the splash reports; Wrench.Host fills it in once it has attached to the JVM. */
public final class WrenchInfo {
    private static volatile String hostVersion;
    private static volatile String runtime;

    private WrenchInfo() {
    }

    public static void setHost(String version, String runtimeDescription) {
        hostVersion = version;
        runtime = runtimeDescription;
    }

    static String hostVersion() {
        return hostVersion;
    }

    static String runtime() {
        return runtime;
    }
}
