import java.util.ArrayList;
import se.krka.kahlua.j2se.J2SEPlatform;
import se.krka.kahlua.vm.JavaFunction;
import se.krka.kahlua.vm.KahluaTable;
import se.krka.kahlua.vm.KahluaThread;
import zombie.Lua.Event;
import zombie.Lua.LuaManager;

// Plays PZ's part: sets up Lua globals, fires OnChallengeQuery, then drives the challenge like ChallengeData does.
public final class LuaDriver {
    public static void run() {
        LuaManager.platform = new J2SEPlatform();
        LuaManager.thread = new KahluaThread();
        LuaManager.env = LuaManager.platform.newTable();
        var env = LuaManager.env;

        var challenges = new ArrayList<KahluaTable>();
        env.rawset("addChallenge", (JavaFunction) (frame, n) -> {
            challenges.add((KahluaTable) frame.get(0));
            return 0;
        });
        var sandbox = table();
        sandbox.rawset("ZombieConfig", table());
        env.rawset("SandboxVars", sandbox);
        var multipliers = table();
        multipliers.rawset("Insane", "2.5");
        env.rawset("ZombiePopulationMultiplier", multipliers);
        var arrayList = table();
        arrayList.rawset("new", (JavaFunction) (frame, n) -> frame.push(new ArrayList<Object>()));
        env.rawset("ArrayList", arrayList);
        var player = new FakePlayer();

        new Event("OnChallengeQuery").trigger(null, null, new Object[0]);
        var challenge = challenges.get(0);
        System.out.println("challenge id=" + challenge.rawget("id") + " name=" + challenge.rawget("name"));
        var spawn = (KahluaTable) ((KahluaTable) challenge.rawget("spawns")).rawget(1.0);
        System.out.println("spawn x=" + spawn.rawget("x") + " z=" + spawn.rawget("z"));

        call(challenge, "OnInitWorld");
        var config = (KahluaTable) sandbox.rawget("MultiplierConfig");
        System.out.println("Zombies=" + sandbox.rawget("Zombies")
                + " PopulationMultiplier=" + ((KahluaTable) sandbox.rawget("ZombieConfig")).rawget("PopulationMultiplier")
                + " XPMultiplierGlobal=" + config.rawget("XPMultiplierGlobal")
                + " XPMultiplierGlobalToggle=" + config.rawget("XPMultiplierGlobalToggle"));

        call(challenge, "AddPlayer", 0.0, player);
        System.out.println("intoxication=" + player.stats.values.get("INTOXICATION")
                + " inventoryCleared=" + player.inventory.cleared + " feet=" + player.feet);

        System.out.println("Add -> " + call(challenge, "Add", 2.0, 40.0));
        var failed = LuaManager.thread.pcall(challenge.rawget("Fail"), new Object[0]);
        System.out.println("Fail -> ok=" + failed[0] + " error=" + failed[1]);
    }

    private static Object call(KahluaTable table, String name, Object... args) {
        var result = LuaManager.thread.pcall(table.rawget(name), args);
        if (!Boolean.TRUE.equals(result[0]))
            throw new IllegalStateException(name + " failed: " + result[1]);
        return result.length > 1 ? result[1] : null;
    }

    private static KahluaTable table() {
        return LuaManager.platform.newTable();
    }
}
