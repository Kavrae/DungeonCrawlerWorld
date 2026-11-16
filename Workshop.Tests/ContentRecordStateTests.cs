using System.Text.Json.Nodes;
using Builders.Editing;
using DungeonCrawlerWorld.Workshop;
using Engine.Content;
using Game.Bootstrap;
using Game.Content;
using Game.Modules.Inventory;
using UIEngine.UI.Filtering;
using Workshop.Editing;

namespace Workshop.Tests;

/// <summary>A record's state, the one rule its row's markers and the browser's filters read, worked out from the items the Items tab holds.</summary>
[TestClass]
public sealed class ContentRecordStateTests
{
    private const string ModItemName = "Health Potion";

    private static readonly Lazy<GameSession> Session = new(() => ContentSession.Build(ContentSession.ValidateMods(Path.Combine(AppContext.BaseDirectory, "NoMods"), WorkshopTestContent.Master, []), WorkshopTestContent.Master, [], TextWriter.Null));

    private ContentWorkspace _workspace = null!;

    private ContentEditingTable<ItemDefinition> Open(Func<ItemDefinition, string?>? modNameOf = null)
    {
        var session = Session.Value;
        var workspace = _workspace = new ContentWorkspace(session);
        var items = new ContentEditingTable<ItemDefinition>(workspace,GameContent.ItemsTable, "Items", session.Catalogs.ItemCatalog.Definitions, item => item.Name, modNameOf ?? (_ => null), session.ContentChecks.ProblemsOf, ActivatableAppearance.Of, ActivatableAppearance.Fields);
        items.EvaluateAll();
        return items;
    }

    private static ContentRecordDocument<ItemDefinition> Named(ContentEditingTable<ItemDefinition> items, string name) =>
        items.Documents.Single(document => document.Original.Name == name);

    private static ContentSchema ItemSchema => Session.Value.Catalogs.ContentTypes.GetValueCodec(typeof(ItemDefinition)).Schema;

    private static void SetGoldValue(ContentEditingTable<ItemDefinition> items, ContentRecordDocument<ItemDefinition> document, JsonNode value) =>
        items.Apply(document, new ContentInspectorEdit("Change Gold value", record => record.SetField(ContentPath.Root, ItemSchema, "gv", value), "gv", ChangesShape: false));

    [TestMethod]
    [DataRow(ContentRecordState.None, null, "")]
    [DataRow(ContentRecordState.Unsaved, null, " *")]
    [DataRow(ContentRecordState.Draft, null, " [Draft]")]
    [DataRow(ContentRecordState.Disabled, null, " [Disabled]")]
    [DataRow(ContentRecordState.Disabled, "Scroll of Healing", " [Disabled][Replaced]")]
    [DataRow(ContentRecordState.Draft, "Scroll of Healing", " [Draft]")]
    [DataRow(ContentRecordState.Deleted, null, " [Deleted]")]
    [DataRow(ContentRecordState.HasErrors, null, " (!)")]
    [DataRow(ContentRecordState.DiffersFromMaster, null, "")]
    [DataRow(ContentRecordState.Unsaved | ContentRecordState.Disabled | ContentRecordState.Draft | ContentRecordState.HasErrors | ContentRecordState.DiffersFromMaster, "Scroll of Healing", " * [Disabled][Replaced][Draft] (!)")]
    [DataRow(ContentRecordState.Unsaved | ContentRecordState.Deleted | ContentRecordState.HasErrors | ContentRecordState.DiffersFromMaster, null, " * [Deleted] (!)")]
    public void EachState_ReadsAsItsMarkers(ContentRecordState state, string? replacementName, string markers)
    {
        Assert.AreEqual(markers, ContentRecordMarkers.Of(state, replacementName));
    }

    [TestMethod]
    public void TheMarkersMeaning_NamesTheReplacement_AndIsNoneForNoMarker()
    {
        var meaning = ContentRecordMarkers.MeaningOf(ContentRecordState.Disabled | ContentRecordState.Draft, "Scroll of Healing");

        CollectionAssert.AreEqual(new[] { "Disabled: kept in master, never loaded", "Replaced by Scroll of Healing", "Draft: not published to master" }, meaning!.Split('\n'));
        Assert.IsNull(ContentRecordMarkers.MeaningOf(ContentRecordState.DiffersFromMaster, null));
    }

    [TestMethod]
    public void OnlyADisabledOrDeletedRecord_IsGreyed()
    {
        Assert.IsTrue(ContentRecordMarkers.IsDimmed(ContentRecordState.Disabled));
        Assert.IsTrue(ContentRecordMarkers.IsDimmed(ContentRecordState.Deleted | ContentRecordState.Unsaved));
        Assert.IsFalse(ContentRecordMarkers.IsDimmed(ContentRecordState.Draft | ContentRecordState.HasErrors | ContentRecordState.Unsaved | ContentRecordState.DiffersFromMaster));
    }

    [TestMethod]
    public void APublishedRecord_IsNone_UntilEdited_AndNoneAgainOnceEditedBack()
    {
        var items = Open();
        var potion = Named(items, "Cure Poison Potion");
        var goldValue = potion.Record.Root["gv"]?.DeepClone();

        Assert.AreEqual(ContentRecordState.None, items.StateOf(potion));

        SetGoldValue(items, potion, JsonValue.Create(999L));
        Assert.AreEqual(ContentRecordState.Unsaved | ContentRecordState.DiffersFromMaster, items.StateOf(potion));

        SetGoldValue(items, potion, goldValue!);
        Assert.AreEqual(ContentRecordState.None, items.StateOf(potion));
    }

    [TestMethod]
    public void AValueThatDoesNotDecode_HasErrors()
    {
        var items = Open();
        var potion = Named(items, "Cure Poison Potion");

        SetGoldValue(items, potion, JsonValue.Create("lots"));

        Assert.IsTrue(items.StateOf(potion).HasFlag(ContentRecordState.HasErrors));
    }

    [TestMethod]
    public void ANewRecord_IsAnUnsavedDraft_ThatDiffersFromMaster()
    {
        var items = Open();
        var potion = Named(items, "Cure Poison Potion");
        var copy = JsonNode.Parse(potion.Record.Json)!.AsObject();
        copy[ContentTable.IdKey] = ContentGuid.Format(Guid.NewGuid());

        var created = items.CreateNew(copy.ToJsonString(), "New item")!;

        Assert.AreEqual(ContentRecordState.Unsaved | ContentRecordState.Draft | ContentRecordState.DiffersFromMaster, items.StateOf(created));
    }

    [TestMethod]
    public void ADisabledRecord_IsDisabled_AndADeletedOne_IsDeleted()
    {
        var items = Open();
        var disabled = Named(items, "Cure Poison Potion");
        var deleted = Named(items, "Wand of Fireball");

        Assert.IsNull(_workspace.Disable(items, disabled.Id, replacement: null));
        items.Delete(deleted.Id, "Delete");

        Assert.AreEqual(ContentRecordState.Unsaved | ContentRecordState.Disabled | ContentRecordState.DiffersFromMaster, items.StateOf(disabled));
        Assert.AreEqual(ContentRecordState.Unsaved | ContentRecordState.Deleted | ContentRecordState.DiffersFromMaster, items.StateOf(deleted));
    }

    [TestMethod]
    public void AModsRecord_IsNoneOfThem()
    {
        var items = Open(item => item.Name == ModItemName ? "ExampleMod" : null);

        Assert.AreEqual(ContentRecordState.None, items.StateOf(Named(items, ModItemName)));
    }

    [TestMethod]
    public void TheToPublishList_IsExactlyTheRecordsThatDifferFromMaster()
    {
        var items = Open();
        SetGoldValue(items, Named(items, "Cure Poison Potion"), JsonValue.Create(999L));
        Assert.IsNull(_workspace.Disable(items, Named(items, "Health Potion").Id, replacement: null));
        items.Delete(Named(items, "Wand of Fireball").Id, "Delete");

        var differing = items.Documents.Concat(items.DeletedDocuments)
            .Where(document => items.StateOf(document).HasFlag(ContentRecordState.DiffersFromMaster))
            .Select(document => document.Id);

        CollectionAssert.AreEquivalent(differing.ToArray(), items.PendingPublish().Select(record => record.RecordId!.Value).ToArray());
        Assert.HasCount(3, items.PendingPublish().ToList());
    }

    [TestMethod]
    [DataRow("show:unsaved", " *")]
    [DataRow("show:draft", "[Draft]")]
    [DataRow("show:errors", " (!)")]
    [DataRow("show:disabled", "[Disabled]")]
    [DataRow("show:deleted", "[Deleted]")]
    public void EachShowOnlyChoice_ListsExactlyTheRowsMarkedWithIt(string choiceId, string marker)
    {
        var items = Open();
        SetGoldValue(items, Named(items, "Cure Poison Potion"), JsonValue.Create("lots"));
        Assert.IsNull(_workspace.Disable(items, Named(items, "Health Potion").Id, replacement: null));
        items.Delete(Named(items, "Wand of Fireball").Id, "Delete");
        var copy = JsonNode.Parse(Named(items, "Scroll of Healing").Record.Json)!.AsObject();
        copy[ContentTable.IdKey] = ContentGuid.Format(Guid.NewGuid());
        items.CreateNew(copy.ToJsonString(), "New item");
        var filter = new ListFilter<ContentRecordDocument<ItemDefinition>>([ContentRecordFilters.ShowOnly(items)]);
        List<ContentRecordDocument<ItemDefinition>> rows = [.. items.Documents, .. items.DeletedDocuments];

        filter.Selection.SetCheckedIds([choiceId]);

        var marked = rows.Where(document => ContentRecordMarkers.Of(items.StateOf(document), null).Contains(marker, StringComparison.Ordinal)).ToList();
        CollectionAssert.AreEquivalent(marked, rows.Where(filter.Matches).ToList());
        Assert.IsNotEmpty(marked, choiceId);
    }

    [TestMethod]
    public void DraftAndHasErrors_ListTheDraftsWithErrors_AndDiffersFromMaster_ListsTheToPublishList()
    {
        var items = Open();
        var brokenDraft = JsonNode.Parse(Named(items, "Scroll of Healing").Record.Json)!.AsObject();
        brokenDraft[ContentTable.IdKey] = ContentGuid.Format(Guid.NewGuid());
        var created = items.CreateNew(brokenDraft.ToJsonString(), "New item")!;
        SetGoldValue(items, created, JsonValue.Create("lots"));
        SetGoldValue(items, Named(items, "Cure Poison Potion"), JsonValue.Create("lots"));
        var filter = new ListFilter<ContentRecordDocument<ItemDefinition>>([ContentRecordFilters.ShowOnly(items)]);

        filter.Selection.SetCheckedIds(["show:draft", "show:errors"]);
        var draftsWithErrors = items.Documents.Where(filter.Matches).ToList();
        filter.Selection.SetCheckedIds(["show:differs"]);
        var differing = items.Documents.Concat(items.DeletedDocuments).Where(filter.Matches).Select(document => document.Id).ToList();

        Assert.AreSame(created, draftsWithErrors.Single());
        CollectionAssert.AreEquivalent(items.PendingPublish().Select(record => record.RecordId!.Value).ToList(), differing);
    }
}
