import java.util.HashMap;
import java.util.Map;

public class FakePlayer {
    public final Stats stats = new Stats();
    public final Inventory inventory = new Inventory();
    public Object feet = "boots";

    public double getHoursSurvived() {
        return 0;
    }

    public Stats getStats() {
        return stats;
    }

    public Inventory getInventory() {
        return inventory;
    }

    public void setClothingItem_Feet(Object item) {
        feet = item;
    }

    @Override
    public String toString() {
        return "FakePlayer";
    }

    public static class Stats {
        public final Map<String, Float> values = new HashMap<>();

        public void set(String stat, float value) {
            values.put(stat, value);
        }
    }

    public static class Inventory {
        public boolean cleared;

        public void clear() {
            cleared = true;
        }
    }
}
