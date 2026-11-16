package zombie.gameStates;

public class GameState {
    public void enter() {
    }

    public void exit() {
    }

    public void render() {
    }

    public GameState redirectState() {
        throw new UnsupportedOperationException("stub");
    }

    public GameStateMachine.StateAction update() {
        throw new UnsupportedOperationException("stub");
    }
}
