package io.pznet.wrench.bench;

/** A grid square stand-in for the batching benchmarks, exposed to Kahlua like the game's classes. */
public final class BenchSquare {
    private final boolean solid;

    public BenchSquare(boolean solid) {
        this.solid = solid;
    }

    public boolean isSolidTrans() {
        return solid;
    }
}
