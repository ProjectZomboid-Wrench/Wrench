using Wrench.Host.Events;
namespace Wrench.Host.Mods;

internal sealed class ModHost : IModHost
{
    public static readonly ModHost Instance = new();

    public IDisposable RegisterLuaEvent(ModInstance mod, string eventName, Action<LuaEventArgs> handler) =>
        LuaEventRouter.Register(mod, eventName, handler);

    public IDisposable RegisterLuaEventBatch(ModInstance mod, string eventName, Action<LuaEventBatch> handler) =>
        LuaEventRouter.RegisterBatch(mod, eventName, handler);

    public void Log(ModInstance mod, LogLevel level, string message, Exception? exception) =>
        Logging.Log.Write(mod.Id, level, message, exception);
}
