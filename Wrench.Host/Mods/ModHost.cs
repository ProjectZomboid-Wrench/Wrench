using Wrench.Host.Events;
namespace Wrench.Host.Mods;

internal sealed class ModHost : IModHost
{
    public static readonly ModHost Instance = new();

    public IDisposable RegisterLuaEvent(ModInstance mod, string eventName, Action<LuaEventArgs> handler) =>
        LuaEventRouter.Register(mod, eventName, handler);

    // stdout, because PZ's launcher redirects it to ~/Zomboid/projectzomboid.sh.log; stderr isn't captured there.
    public void Log(ModInstance mod, ModLogLevel level, string message, Exception? exception) =>
        Console.WriteLine(exception is null
            ? $"[{mod.Id}] {level.ToString().ToUpperInvariant()}: {message}"
            : $"[{mod.Id}] {level.ToString().ToUpperInvariant()}: {message}{Environment.NewLine}{exception}");
}
