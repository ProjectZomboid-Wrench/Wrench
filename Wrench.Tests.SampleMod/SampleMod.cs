using Wrench;

namespace Wrench.Tests.SampleMod;

public sealed class SampleMod : ModInstance
{
    private GameObject? _kept;

    public override void OnLoad()
    {
        RegisterLuaEvent(LuaEvents.OnGameBoot, e =>
        {
            Log.Info($"OnGameBoot({string.Join(", ", e.Values.Select(Describe))})");
            Log.Info($"second arg as double: {e.Get<double>(1)}");
            _kept = e[3] as GameObject;
        });

        RegisterLuaEvent(LuaEvents.OnMainMenuEnter, () =>
        {
            Log.Info(_kept is { IsValid: false } ? "kept GameObject expired as expected" : "kept GameObject still valid");
            try
            {
                _ = _kept?.TypeName;
            }
            catch (ObjectDisposedException e)
            {
                Log.Info($"expired use: {e.Message}");
            }
        });
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        GameObject go => $"{go.TypeName}:{go}",
        _ => value.ToString()!,
    };
}

public sealed class BrokenMod : ModInstance
{
    public override void OnLoad() =>
        RegisterLuaEvent(LuaEvents.OnGameBoot, () => throw new InvalidOperationException("boom"));
}
