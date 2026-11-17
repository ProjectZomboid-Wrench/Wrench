using Zomboid.Java.Characters;
using Zomboid.Lua;

namespace Wrench.Tests.SampleMod;

/// <summary>A C# subclass of a vanilla action other than the base, calling through to vanilla's isValid.</summary>
internal sealed class LoggedTransfer(IsoGameCharacter character) : ISInventoryTransferAction(new LuaNew(character))
{
    public override bool IsValid()
    {
        Set("log", "C# isValid");
        return base.IsValid();
    }
}
