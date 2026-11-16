using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wrench.Host.Security;

namespace Wrench.Host.Tests;

[TestClass]
public class TrustStoreTest
{
    private readonly string _path = Path.Combine(Directory.CreateTempSubdirectory("wrench-trust-").FullName, "Wrench", "trust.json");

    [TestMethod]
    public void Decisions_Persist_For_The_Same_Code()
    {
        new TrustStore(_path).Record("Mod", allowed: true, "HASH", [ModPermissions.Full]);

        Assert.AreEqual(TrustStore.Decision.Allowed, new TrustStore(_path).Lookup("Mod", "HASH", [ModPermissions.Full]));
        Assert.AreEqual(TrustStore.Decision.Undecided, new TrustStore(_path).Lookup("Other", "HASH", [ModPermissions.Full]));
    }

    [TestMethod]
    public void Changed_Code_Or_New_Permissions_Ask_Again()
    {
        var store = new TrustStore(_path);
        store.Record("Mod", allowed: false, "HASH", [ModPermissions.Full]);

        Assert.AreEqual(TrustStore.Decision.Denied, store.Lookup("Mod", "HASH", [ModPermissions.Full]));
        Assert.AreEqual(TrustStore.Decision.Undecided, store.Lookup("Mod", "UPDATED", [ModPermissions.Full]));
        Assert.AreEqual(TrustStore.Decision.Undecided, store.Lookup("Mod", "HASH", [ModPermissions.Full, "storage"]));
    }

    [TestMethod]
    public void A_Corrupt_File_Means_Undecided()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ not json");

        Assert.AreEqual(TrustStore.Decision.Undecided, new TrustStore(_path).Lookup("Mod", "HASH", [ModPermissions.Full]));
    }

    [TestMethod]
    public void Unknown_Permissions_Are_Reported()
    {
        var (known, unknown) = ModPermissions.Parse("full, everything ,full");

        CollectionAssert.AreEqual(new[] { ModPermissions.Full }, known);
        CollectionAssert.AreEqual(new[] { "everything" }, unknown);
    }
}
