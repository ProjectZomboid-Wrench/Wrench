import java.io.File;
import java.util.ArrayList;
import java.util.List;
import se.krka.kahlua.j2se.J2SEPlatform;
import se.krka.kahlua.vm.JavaFunction;
import se.krka.kahlua.vm.KahluaTable;
import se.krka.kahlua.vm.KahluaThread;
import zombie.Lua.Event;
import zombie.Lua.LuaManager;

// Plays PZ's part: sets up Lua globals, fires OnChallengeQuery, then drives the challenge like ChallengeData does.
public final class LuaDriver {
    private static final ArrayList<KahluaTable> challenges = new ArrayList<>();
    private static List<String> activeMods = List.of("PlainLuaMod", "WrenchTestMod", "WrenchFullTrustMod");

    /** A fresh Lua state, as PZ has after (re)loading its scripts. */
    public static void setup(String originalOption) {
        LuaManager.platform = new J2SEPlatform();
        LuaManager.thread = new KahluaThread();
        LuaManager.env = LuaManager.platform.newTable();
        var env = LuaManager.env;

        env.rawset("addChallenge", (JavaFunction) (frame, n) -> {
            challenges.add((KahluaTable) frame.get(0));
            return 0;
        });

        var menu = table();
        menu.rawset("createMenu", (JavaFunction) (frame, n) -> {
            var options = new ArrayList<Object>();
            options.add(originalOption);
            return frame.push(options);
        });
        env.rawset("ContextMenuBuilder", menu);

        var tutorial = table();
        tutorial.rawset("lockDoor", (JavaFunction) (frame, n) -> frame.push("locked in Lua"));
        env.rawset("Tutorial1", tutorial);

        // A Lua OOP-style object: its methods expect self as the first argument.
        var steps = table();
        var step = new StringBuilder("WelcomeStep");
        steps.rawset("get", (JavaFunction) (frame, n) -> {
            if (frame.get(0) != steps)
                throw new RuntimeException("steps:get called without self");
            return frame.push(step);
        });
        env.rawset("Steps", steps);

        var items = table();
        items.rawset(1.0, "a");
        items.rawset(2.0, "b");
        items.rawset(3.0, "c");
        env.rawset("Items", items);

        env.rawset("getActivatedMods", (JavaFunction) (frame, n) -> frame.push(new ArrayList<>(activeMods)));
        env.rawset("getModInfoByID", (JavaFunction) (frame, n) -> frame.push(modInfo((String) frame.get(0))));
        env.rawset("getMyDocumentFolder", (JavaFunction) (frame, n) -> frame.push(System.getProperty("wrench.test.home")));
    }

    private static FakeModInfo modInfo(String id) {
        var root = new File(System.getProperty("wrench.test.pzmods"), id);
        return root.isDirectory() ? new FakeModInfo(root) : null;
    }

    /**
     * Lists the agent's pending permission requests and answers for WrenchFullTrustMod through
     * WrenchPermissions.decide, the path the prompt game state uses. Also shows Lua has nothing to call.
     */
    public static void trustFlow() throws ReflectiveOperationException {
        var permissions = Class.forName("io.pznet.wrench.WrenchPermissions");
        for (var request : (java.util.List<?>) permissions.getMethod("pending").invoke(null)) {
            var id = request.getClass().getMethod("id").invoke(request);
            var descriptions = (String[]) request.getClass().getMethod("descriptions").invoke(request);
            System.out.println("pending: " + id + " [" + descriptions[0] + "]");
        }

        var host = (KahluaTable) LuaManager.env.rawget("WrenchHost");
        System.out.println("lua can decide: " + (host.rawget("decide") != null) + ", lua sees pending: " + (host.rawget("pending") != null));

        var decision = System.getProperty("wrench.test.decision");
        if (decision == null)
            return;
        var decide = permissions.getDeclaredMethod("decide", String.class, boolean.class);
        decide.setAccessible(true);
        decide.invoke(null, "WrenchFullTrustMod", decision.equals("allow"));
        System.out.println("decided " + decision);
    }

    /** Enables, disables and re-enables WrenchTestMod across Lua reloads, pinging it each time. */
    public static void pzModLifecycle() {
        ping();
        activeMods = List.of("PlainLuaMod");
        reload();
        ping();
        activeMods = List.of("PlainLuaMod", "WrenchTestMod");
        reload();
        ping();
    }

    private static void ping() {
        System.out.println("pinging with active mods " + activeMods);
        new Event("OnTestPing").trigger(null, null, new Object[0]);
    }

    /** Calls the patched functions the way PZ's UI code would. */
    public static void callPatched(String player) {
        var menu = (KahluaTable) LuaManager.env.rawget("ContextMenuBuilder");
        System.out.println("createMenu -> " + call(menu, "createMenu", player));
        var tutorial = (KahluaTable) LuaManager.env.rawget("Tutorial1");
        System.out.println("lockDoor -> " + call(tutorial, "lockDoor", 131.0, 181.0, 0.0));
    }

    /**
     * Runs a craftRecipe's OnCreate the way B42 does: LuaManager.getFunctionObject walks the dotted name through
     * tables and accepts a JavaFunction or LuaClosure, then CraftRecipeData calls it with itself and the character.
     */
    public static void craftOnCreate(String functionName) {
        Object current = LuaManager.env;
        for (var part : functionName.split("\\.")) {
            if (!(current instanceof KahluaTable table)) {
                System.out.println("no such function \"" + functionName + "\"");
                return;
            }
            current = table.rawget(part);
        }
        if (!(current instanceof JavaFunction)) {
            System.out.println("no such function \"" + functionName + "\"");
            return;
        }
        var craftData = new FakeCraftRecipeData();
        var result = LuaManager.thread.pcall(current, new Object[] {craftData, new FakePlayer()});
        System.out.println("OnCreate " + functionName + " -> " + result[0] + " " + craftData.log);
    }

    /** Calls a .NET function taking a typed JavaList with a real java.util.ArrayList. */
    public static void typedCall(String functionName) {
        var functions = (KahluaTable) LuaManager.env.rawget("WrenchTest");
        var items = new ArrayList<Object>(List.of("rice", "plate"));
        var result = LuaManager.thread.pcall(functions.rawget(functionName), new Object[] {items});
        System.out.println("typed " + functionName + " -> " + result[0] + " " + items);
    }

    /** Like Core.ResetLua: every global is recreated, then OnGameBoot fires again. */
    public static void reload() {
        setup("reloaded original");
        new Event("OnGameBoot").trigger(null, null, new Object[] {"hi", 42, null, new StringBuilder("sb")});
    }

    public static void run() {
        var env = LuaManager.env;
        var host = (KahluaTable) env.rawget("WrenchHost");
        System.out.println("WrenchHost.version=" + (host == null ? "missing" : host.rawget("version")));

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
