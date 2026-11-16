import java.io.File;

// Stand-in for zombie.gameStates.ChooseGameInfo$Mod: the folders of a B42-style mod (<mod>/42 and <mod>/common).
public class FakeModInfo {
    private final File root;

    public FakeModInfo(File root) {
        this.root = root;
    }

    public String getName() {
        return root.getName();
    }

    public String getDir() {
        return root.getPath();
    }

    public String getVersionDir() {
        return new File(root, "42").getPath();
    }

    public String getCommonDir() {
        return new File(root, "common").getPath();
    }
}
