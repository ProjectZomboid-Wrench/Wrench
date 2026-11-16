import java.util.ArrayList;
import java.util.List;

/** Stand-in for zombie.entity.components.crafting.recipe.CraftRecipeData, as passed to OnCreate. */
public class FakeCraftRecipeData {
    public final List<String> log = new ArrayList<>();

    public void addLog(String message) {
        log.add(message);
    }
}
