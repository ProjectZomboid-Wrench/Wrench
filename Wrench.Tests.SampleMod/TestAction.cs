using Zomboid.Java.Characters;
using Zomboid.Lua.TimedActions;

namespace Wrench.Tests.SampleMod;

/// <summary>A timed action that records its lifecycle in its table's <c>log</c> field for the driver to print.</summary>
internal sealed class TestAction : TimedAction
{
    private int _updates;

    public TestAction(IsoGameCharacter character, string label) : base(character)
    {
        MaxTime = 50;
        StopOnWalk = false;
        Set("label", label);
    }

    public override bool IsValid() => true;

    public override void Start()
    {
        Note($"start {Get<string>("label")}");
        SetActionAnim("Loot");
    }

    public override void Update()
    {
        _updates++;
        base.Update();
    }

    public override void Perform()
    {
        Note($"perform after {_updates} updates");
        base.Perform();
    }

    public override bool Complete()
    {
        Note($"complete after {Character.GetHoursSurvived()} hours, stopOnWalk={StopOnWalk}");
        return true;
    }

    public override void Stop()
    {
        Note("stop");
        base.Stop();
    }

    private void Note(string text)
    {
        var log = Get<string?>("log");
        Set("log", log is null ? text : $"{log}; {text}");
    }
}
