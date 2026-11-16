import zombie.Lua.Event;

public class Hello {
    public static void main(String[] args) {
        System.out.println("Hello.main ran");
        new Event("OnGameBoot").trigger(null, null, new Object[] {"hi", 42, null, new StringBuilder("sb")});
        new Event("OnTick").trigger(null, null, new Object[] {1.5});
        new Event("OnMainMenuEnter").trigger(null, null, null);
        LuaDriver.run();
    }
}
