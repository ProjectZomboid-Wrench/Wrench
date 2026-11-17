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
        env.rawset("isClient", (JavaFunction) (frame, n) -> frame.push(Boolean.FALSE));
        timedActionClasses();
        contextMenuClasses();
    }

    private static KahluaTable rootMenu;

    // ISContextMenu as far as the menu API uses it: options are a 1-based sequence, submenus live in the root's instanceMap.
    private static void contextMenuClasses() {
        var env = LuaManager.env;
        var menuClass = table();
        menuClass.rawset("__index", menuClass);
        menuClass.rawset("Type", "ISContextMenu");
        menuClass.rawset("addOption", (JavaFunction) (frame, n) -> {
            var self = (KahluaTable) frame.get(0);
            var option = table();
            option.rawset("name", frame.get(1));
            option.rawset("target", frame.get(2));
            option.rawset("onSelect", frame.get(3));
            for (var i = 4; i < n; i++)
                option.rawset("param" + (i - 3), frame.get(i));
            var options = (KahluaTable) self.rawget("options");
            options.rawset((double) (size(options) + 1), option);
            return frame.push(option);
        });
        menuClass.rawset("getOptionFromName", (JavaFunction) (frame, n) -> {
            var options = (KahluaTable) ((KahluaTable) frame.get(0)).rawget("options");
            for (var i = 1; i <= size(options); i++) {
                var option = (KahluaTable) options.rawget((double) i);
                if (option.rawget("name").equals(frame.get(1)))
                    return frame.push(option);
            }
            return 0;
        });
        menuClass.rawset("removeOptionByName", (JavaFunction) (frame, n) -> {
            var options = (KahluaTable) ((KahluaTable) frame.get(0)).rawget("options");
            var count = size(options);
            for (var i = 1; i <= count; i++) {
                if (((KahluaTable) options.rawget((double) i)).rawget("name").equals(frame.get(1))) {
                    for (var j = i; j < count; j++)
                        options.rawset((double) j, options.rawget((double) (j + 1)));
                    options.rawset((double) count, null);
                    break;
                }
            }
            return 0;
        });
        menuClass.rawset("getNew", (JavaFunction) (frame, n) -> frame.push(newMenu(menuClass)));
        menuClass.rawset("addSubMenu", (JavaFunction) (frame, n) -> {
            var map = (KahluaTable) rootMenu.rawget("instanceMap");
            var number = (double) (size(map) + 1);
            map.rawset(number, frame.get(2));
            ((KahluaTable) frame.get(1)).rawset("subOption", number);
            return 0;
        });
        menuClass.rawset("getSubMenu", (JavaFunction) (frame, n) ->
                frame.push(((KahluaTable) rootMenu.rawget("instanceMap")).rawget(frame.get(1))));
        rootMenu = newMenu(menuClass);
        env.rawset("getPlayerContextMenu", (JavaFunction) (frame, n) -> frame.push(rootMenu));

        var pane = table();
        pane.rawset("addToolTip", (JavaFunction) (frame, n) -> frame.push(table()));
        env.rawset("ISInventoryPaneContextMenu", pane);
        // Like PZ, text that isn't a known key comes back unchanged.
        env.rawset("getText", (JavaFunction) (frame, n) -> {
            var key = (String) frame.get(0);
            return frame.push(key.contains("_") ? "<" + key + ">" : key);
        });
        env.rawset("getSpecificPlayer", (JavaFunction) (frame, n) -> frame.push(new FakePlayer()));
    }

    private static KahluaTable newMenu(KahluaTable menuClass) {
        var menu = table();
        menu.setMetatable(menuClass);
        menu.rawset("options", table());
        menu.rawset("instanceMap", table());
        return menu;
    }

    private static int size(KahluaTable sequence) {
        var n = 0;
        while (sequence.rawget((double) (n + 1)) != null)
            n++;
        return n;
    }

    /**
     * Opens the inventory context menu on a few items the way ISInventoryPaneContextMenu.createMenu does (vanilla's
     * "Eat" first, then the event), prints it, then clicks every option the way ISContextMenu does.
     */
    public static void inventoryMenu() {
        var plate = new FakeItem("Test.Plate", "plate");
        var cold = new FakeItem("Test.Plate", "cold");
        var spoon = new FakeItem("Test.Spoon", "spoon");
        var stack = table();
        var stackItems = table();
        stackItems.rawset(1.0, cold);
        stackItems.rawset(2.0, cold);
        stack.rawset("items", stackItems);
        var items = table();
        items.rawset(1.0, plate);
        items.rawset(2.0, stack);
        items.rawset(3.0, spoon);

        var context = rootMenu;
        ((KahluaTable) context.rawget("options")).rawset(1.0, null);
        LuaManager.thread.pcall(LuaManager.thread.tableget(context, "addOption"), new Object[] {context, "<ContextMenu_Eat>", plate,
                (JavaFunction) (frame, n) -> {
                    ((FakeItem) frame.get(0)).setName("eaten " + frame.get(0));
                    return 0;
                }});
        new Event("OnFillInventoryObjectContextMenu").trigger(null, null, new Object[] {0.0, context, items});

        printMenu("menu", context);
        var taste = ((KahluaTable) LuaManager.env.rawget("WrenchTest")).rawget("Taste");
        LuaManager.thread.pcall(taste, new Object[] {"open"});
        printMenu("opened", context);
        LuaManager.thread.pcall(taste, new Object[] {"hidden"});
        printMenu("hidden", context);
        clickAll(context);
        System.out.println("after clicks: " + plate + ", " + cold + ", " + spoon);
    }

    private static void printMenu(String label, KahluaTable menu) {
        var options = (KahluaTable) menu.rawget("options");
        var lines = new ArrayList<String>();
        for (var i = 1; i <= size(options); i++) {
            var option = (KahluaTable) options.rawget((double) i);
            var tooltip = (KahluaTable) option.rawget("toolTip");
            lines.add(option.rawget("name") + (Boolean.TRUE.equals(option.rawget("notAvailable")) ? " (disabled)" : "")
                    + (tooltip != null ? " tooltip=" + tooltip.rawget("description") : "")
                    + (option.rawget("subOption") != null ? " submenu" : ""));
        }
        System.out.println(label + ": " + String.join(" | ", lines));
        for (var i = 1; i <= size(options); i++) {
            var sub = ((KahluaTable) options.rawget((double) i)).rawget("subOption");
            if (sub != null)
                printMenu(label + " > " + ((KahluaTable) options.rawget((double) i)).rawget("name"),
                        (KahluaTable) ((KahluaTable) rootMenu.rawget("instanceMap")).rawget(sub));
        }
    }

    private static void clickAll(KahluaTable menu) {
        var options = (KahluaTable) menu.rawget("options");
        for (var i = 1; i <= size(options); i++) {
            var option = (KahluaTable) options.rawget((double) i);
            var sub = option.rawget("subOption");
            if (sub != null) {
                clickAll((KahluaTable) ((KahluaTable) rootMenu.rawget("instanceMap")).rawget(sub));
                continue;
            }
            if (option.rawget("onSelect") == null || Boolean.TRUE.equals(option.rawget("notAvailable")))
                continue;
            var args = new Object[11];
            args[0] = option.rawget("target");
            for (var p = 1; p <= 10; p++)
                args[p] = option.rawget("param" + p);
            var result = LuaManager.thread.pcall(option.rawget("onSelect"), args);
            if (!Boolean.TRUE.equals(result[0]))
                System.out.println("click " + option.rawget("name") + " failed: " + result[1]);
        }
    }

    private static final ArrayList<KahluaTable> actionQueue = new ArrayList<>();

    // ISBaseObject:derive / ISBaseTimedAction:new as vanilla writes them, plus the base methods C# actions rely on.
    private static void timedActionClasses() {
        actionQueue.clear();
        var base = table();
        base.rawset("Type", "ISBaseTimedAction");
        base.rawset("derive", (JavaFunction) (frame, n) -> {
            var self = (KahluaTable) frame.get(0);
            var derived = table();
            derived.setMetatable(self);
            self.rawset("__index", self);
            derived.rawset("Type", frame.get(1));
            System.out.println("derived " + frame.get(1));
            return frame.push(derived);
        });
        base.rawset("new", (JavaFunction) (frame, n) -> {
            var self = (KahluaTable) frame.get(0);
            var action = table();
            action.setMetatable(self);
            self.rawset("__index", self);
            action.rawset("character", frame.get(1));
            action.rawset("maxTime", -1.0);
            action.rawset("stopOnWalk", Boolean.TRUE);
            return frame.push(action);
        });
        base.rawset("isValidStart", (JavaFunction) (frame, n) -> frame.push(Boolean.TRUE));
        base.rawset("waitToStart", (JavaFunction) (frame, n) -> frame.push(Boolean.FALSE));
        base.rawset("update", (JavaFunction) (frame, n) -> {
            note((KahluaTable) frame.get(0), "base update");
            return 0;
        });
        base.rawset("perform", (JavaFunction) (frame, n) -> {
            note((KahluaTable) frame.get(0), "base perform");
            actionQueue.remove(frame.get(0));
            return 0;
        });
        base.rawset("stop", (JavaFunction) (frame, n) -> {
            note((KahluaTable) frame.get(0), "base stop");
            actionQueue.clear();
            return 0;
        });
        base.rawset("forceCancel", (JavaFunction) (frame, n) -> 0);
        base.rawset("setActionAnim", (JavaFunction) (frame, n) -> {
            note((KahluaTable) frame.get(0), "anim " + frame.get(1));
            return 0;
        });
        LuaManager.env.rawset("ISBaseTimedAction", base);

        // A vanilla subclass, made the way vanilla makes them.
        var transfer = (KahluaTable) LuaManager.thread.pcall(base.rawget("derive"), new Object[] {base, "ISInventoryTransferAction"})[1];
        transfer.rawset("isValid", (JavaFunction) (frame, n) -> {
            note((KahluaTable) frame.get(0), "vanilla transfer isValid");
            return frame.push(Boolean.TRUE);
        });
        LuaManager.env.rawset("ISInventoryTransferAction", transfer);

        var queue = table();
        queue.rawset("add", (JavaFunction) (frame, n) -> {
            actionQueue.add((KahluaTable) frame.get(0));
            return frame.push(queue);
        });
        LuaManager.env.rawset("ISTimedActionQueue", queue);
    }

    private static void note(KahluaTable action, String event) {
        var log = action.rawget("log");
        action.rawset("log", log == null ? event : log + "; " + event);
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

    /**
     * Queues an action written in C# through WrenchTest.QueueAction, then runs it the way LuaTimedActionNew and the
     * queue do: one to completion, one interrupted. Calls after it ended fall back to the Lua base class.
     */
    public static void timedActions() {
        var functions = (KahluaTable) LuaManager.env.rawget("WrenchTest");
        var queueAction = functions.rawget("QueueAction");
        LuaManager.thread.pcall(queueAction, new Object[] {new FakePlayer(), "finished"});
        var finished = actionQueue.get(0);
        System.out.println("action " + LuaManager.thread.tableget(finished, "Type") + " maxTime=" + LuaManager.thread.tableget(finished, "maxTime")
                + " isValidStart=" + method(finished, "isValidStart") + " waitToStart=" + method(finished, "waitToStart"));
        method(finished, "start");
        method(finished, "update");
        method(finished, "update");
        var valid = method(finished, "isValid");
        method(finished, "perform");
        var completed = method(finished, "complete");
        System.out.println("finished: isValid=" + valid + " complete=" + completed + " queued=" + actionQueue.size()
                + " log=[" + finished.rawget("log") + "] afterwards isValid=" + method(finished, "isValid"));

        LuaManager.thread.pcall(queueAction, new Object[] {new FakePlayer(), "interrupted"});
        var interrupted = actionQueue.get(0);
        method(interrupted, "start");
        method(interrupted, "stop");
        System.out.println("interrupted: queued=" + actionQueue.size() + " log=[" + interrupted.rawget("log") + "]");
    }

    /**
     * Hands C# a vanilla action (a table of a vanilla subclass) through a wrapper-typed parameter, has C# subclass that
     * vanilla class and runs the result, and passes a Lua sequence to a C# list parameter.
     */
    public static void vanillaClasses() {
        var functions = (KahluaTable) LuaManager.env.rawget("WrenchTest");
        var transfer = (KahluaTable) LuaManager.env.rawget("ISInventoryTransferAction");
        var vanilla = (KahluaTable) LuaManager.thread.pcall(LuaManager.thread.tableget(transfer, "new"), new Object[] {transfer, new FakePlayer()})[1];
        System.out.println("inspect -> " + LuaManager.thread.pcall(functions.rawget("Inspect"), new Object[] {vanilla})[1]
                + " log=[" + vanilla.rawget("log") + "]");

        LuaManager.thread.pcall(functions.rawget("Transfer"), new Object[] {new FakePlayer()});
        var subclassed = actionQueue.get(actionQueue.size() - 1);
        System.out.println("subclassed " + LuaManager.thread.tableget(subclassed, "Type") + ": isValid=" + method(subclassed, "isValid")
                + " log=[" + subclassed.rawget("log") + "]");
        actionQueue.clear();

        var result = LuaManager.thread.pcall(functions.rawget("Sequence"), new Object[] {LuaManager.env.rawget("Items")});
        System.out.println("sequence -> " + result[1]);
        var union = functions.rawget("Union");
        System.out.println("union -> " + LuaManager.thread.pcall(union, new Object[] {"hi"})[1] + " | "
                + LuaManager.thread.pcall(union, new Object[] {2.5})[1] + " | " + LuaManager.thread.pcall(union, new Object[] {vanilla})[1]);
    }

    private static Object method(KahluaTable self, String name) {
        var result = LuaManager.thread.pcall(LuaManager.thread.tableget(self, name), new Object[] {self});
        if (!Boolean.TRUE.equals(result[0]))
            return "error: " + result[1];
        return result.length > 1 ? result[1] : null;
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
