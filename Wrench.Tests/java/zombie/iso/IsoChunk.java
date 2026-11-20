package zombie.iso;

import zombie.Lua.Event;

// Stand-in for PZ's IsoChunk: doLoadGridsquare fires LoadGridsquare once per square, as the game's does per chunk.
public final class IsoChunk {
    private final Object[] squares;

    public IsoChunk(Object... squares) {
        this.squares = squares;
    }

    public void doLoadGridsquare() {
        for (var square : squares)
            new Event("LoadGridsquare").trigger(null, null, new Object[] {square});
    }
}
