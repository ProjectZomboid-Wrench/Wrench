// Stand-in for an IsoGridSquare, for batched LoadGridsquare.
public final class FakeSquare {
    private final String name;
    private final boolean solid;

    public FakeSquare(String name, boolean solid) {
        this.name = name;
        this.solid = solid;
    }

    public boolean isSolidTrans() {
        return solid;
    }

    @Override
    public String toString() {
        return name;
    }
}
