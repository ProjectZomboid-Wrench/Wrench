package zombie.iso;

import zombie.Lua.Event;

// Stand-in for PZ's IsoChunk: doLoadGridsquare fires LoadGridsquare per square, then hands the chunk on, as the game's does.
public final class IsoChunk {
    private final Object[] squares;

    public IsoChunk(Object... squares) {
        this.squares = squares;
    }

    public void doLoadGridsquare() {
        for (var square : squares)
            new Event("LoadGridsquare").trigger(null, null, new Object[] {square});
        zombie.ReanimatedPlayers.instance.addReanimatedPlayersToChunk(this);
        // The game registers the chunk with collision and pathfinding here; batch handlers must have run by now.
        System.out.println("IsoChunk added to the world");
    }
}
