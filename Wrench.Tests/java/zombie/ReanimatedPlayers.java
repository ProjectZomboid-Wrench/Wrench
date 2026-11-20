package zombie;

// Stand-in for PZ's ReanimatedPlayers, whose addReanimatedPlayersToChunk follows IsoChunk's square loop.
public final class ReanimatedPlayers {
    public static final ReanimatedPlayers instance = new ReanimatedPlayers();

    public void addReanimatedPlayersToChunk(Object chunk) {
    }
}
