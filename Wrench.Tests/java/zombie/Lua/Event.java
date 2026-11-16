package zombie.Lua;

// Stand-in for PZ's zombie.Lua.Event with the same trigger shape (KahluaTable/LuaCaller replaced by Object).
public final class Event {
    public String name;

    public Event(String name) {
        this.name = name;
    }

    public boolean trigger(Object env, Object caller, Object[] args) {
        System.out.println("Event.trigger ran: " + name);
        return true;
    }
}
