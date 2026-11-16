using UIEngine.UI.Filtering;
using Workshop.Browsing;

namespace Workshop.Tests;

[TestClass]
public sealed class BrowserFilterStoreTests
{
    private string _directory = null!;

    private string FilePath => Path.Combine(_directory, "Game", "browser-filters.json");

    [TestInitialize]
    public void CreateDirectory() => Directory.CreateDirectory(_directory = Path.Combine(Path.GetTempPath(), $"browser-filters-{Guid.NewGuid():N}"));

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public void EachTabsCheckedIds_ComeBackInANewStore_ApartFromEveryOtherTabs()
    {
        var store = new BrowserFilterStore(FilePath);
        store.Save("Items", ["show:draft", "source:mod:ExampleMod"]);
        store.Save("Actions", ["timing:delayed"]);

        var reopened = new BrowserFilterStore(FilePath);

        CollectionAssert.AreEqual(new[] { "show:draft", "source:mod:ExampleMod" }, reopened.CheckedIdsFor("Items").ToArray());
        CollectionAssert.AreEqual(new[] { "timing:delayed" }, reopened.CheckedIdsFor("Actions").ToArray());
        Assert.IsEmpty(reopened.CheckedIdsFor("Auras"));
        StringAssert.Contains(File.ReadAllText(FilePath), $"\"Version\": {BrowserFilterStore.Version}");
    }

    [TestMethod]
    public void NothingChecked_IsForgotten()
    {
        var store = new BrowserFilterStore(FilePath);
        store.Save("Items", ["show:draft"]);
        store.Save("Items", []);

        Assert.IsEmpty(new BrowserFilterStore(FilePath).CheckedIdsFor("Items"));
        Assert.IsFalse(File.ReadAllText(FilePath).Contains("Items", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AMissingFile_ChecksNothing()
    {
        Assert.IsEmpty(new BrowserFilterStore(FilePath).CheckedIdsFor("Items"));
        Assert.IsFalse(File.Exists(FilePath));
    }

    [TestMethod]
    public void AnUnreadableFile_ChecksNothing_AndTheNextSaveWritesOverIt()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not json");
        var store = new BrowserFilterStore(FilePath);

        Assert.IsEmpty(store.CheckedIdsFor("Items"));

        store.Save("Items", ["show:draft"]);
        CollectionAssert.AreEqual(new[] { "show:draft" }, new BrowserFilterStore(FilePath).CheckedIdsFor("Items").ToArray());
    }

    [TestMethod]
    public void AnIdTheTabNoLongerOffers_IsLeftOut_AndForgottenByTheNextSave()
    {
        var store = new BrowserFilterStore(FilePath);
        store.Save("Items", ["show:draft", "source:mod:GoneMod"]);
        var selection = new ListFilterSelection(["show:draft", "show:errors", "source:master"]);

        selection.SetCheckedIds(new BrowserFilterStore(FilePath).CheckedIdsFor("Items"));
        store.Save("Items", selection.CheckedIds());

        CollectionAssert.AreEqual(new[] { "show:draft" }, selection.CheckedIds().ToArray());
        CollectionAssert.AreEqual(new[] { "show:draft" }, new BrowserFilterStore(FilePath).CheckedIdsFor("Items").ToArray());
    }

    [TestMethod]
    public void EachGame_HasAFileOfItsOwn_BesideItsLaunchOptions()
    {
        var path = BrowserFilterStore.FilePathForGame("DungeonCrawlerWorld");

        Assert.AreEqual("browser-filters.json", Path.GetFileName(path));
        Assert.AreEqual(Path.GetDirectoryName(Debug.DebugLaunchOptionsStore.FilePathForGame("DungeonCrawlerWorld")), Path.GetDirectoryName(path));
    }
}
