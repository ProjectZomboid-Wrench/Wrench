import zombie.Lua.Event;

public class Hello {
    public static void main(String[] args) throws Exception {
        LuaDriver.setup("original option");
        System.out.println("Hello.main ran");
        new Event("OnGameBoot").trigger(null, null, new Object[] {"hi", 42, null, new StringBuilder("sb")});
        new Event("OnTick").trigger(null, null, new Object[] {1.5});
        new Event("OnMainMenuEnter").trigger(null, null, null);
        LuaDriver.trustFlow();
        LuaDriver.run();
        LuaDriver.callPatched("player0");
        LuaDriver.reload();
        LuaDriver.callPatched("player1");
        LuaDriver.craftOnCreate("WrenchTest.OnCreate.MakePlate");
        LuaDriver.pzModLifecycle();
    }
}
