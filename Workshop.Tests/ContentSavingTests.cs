using System.Text.Json.Nodes;
using Builders.Editing;
using Builders.Items;
using DungeonCrawlerWorld.Workshop;
using Engine.Content;
using Game.Bootstrap;
using Game.Content;
using Game.Modules.Inventory;
using Workshop.Editing;

namespace Workshop.Tests;

/// <summary>Saving the Items table into a copy of master: where each record goes, which lines change, and what a rebuild keeps.</summary>
[TestClass]
public sealed class ContentSavingTests
{
    private static readonly Lazy<GameSession> Session = new(() => ContentSession.Build(ContentSession.ValidateMods(Path.Combine(AppContext.BaseDirectory, "NoMods"), WorkshopTestContent.Master, []), WorkshopTestContent.Master, [], TextWriter.Null));

    private string _root = null!;
    private string _masterDirectory = null!;
    private string _draftsDirectory = null!;
    private Func<IReadOnlyDictionary<string, string>, ContentTrialOutcome>? _trialOverride;

    [TestInitialize]
    public void CopyMaster()
    {
        _root = Path.Combine(Path.GetTempPath(), "WorkshopContentSaving", Guid.NewGuid().ToString("N"));
        _masterDirectory = Path.Combine(_root, "Master");
        _draftsDirectory = Path.Combine(_root, "Drafts");
        foreach (var path in Directory.GetFiles(GameContent.DefaultMasterDirectory, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_masterDirectory, Path.GetRelativePath(GameContent.DefaultMasterDirectory, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target);
        }
    }

    [TestCleanup]
    public void DeleteCopy()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string MasterItemsPath => Path.Combine(_masterDirectory, GameContent.ItemsFileName);

    private string DraftItemsPath => Path.Combine(_draftsDirectory, GameContent.ItemsFileName);

    private (ContentEditing Editing, ContentEditingTable<ItemDefinition> Items) Open() => Open(_ => null, []);

    /// <summary>The workspace over the session's items and modItems, each item modOf names taken as that mod's winning record.</summary>
    private (ContentEditing Editing, ContentEditingTable<ItemDefinition> Items) Open(Func<ItemDefinition, string?> modOf, IReadOnlyList<ItemDefinition> modItems)
    {
        var saving = new ContentSaving(
            table => new ContentTableFiles(table, Path.Combine(_masterDirectory, GameContent.FileNameOf(table)), Path.Combine(_draftsDirectory, GameContent.FileNameOf(table))),
            () => ContentSession.ReadMasterFiles(_masterDirectory),
            files => _trialOverride?.Invoke(files) ?? ContentSession.TrialBuild(files, []),
            () => Session.Value);
        var session = Session.Value;
        var workspace = new ContentWorkspace(session, saving);
        var items = new ContentEditingTable<ItemDefinition>(workspace, GameContent.ItemsTable, "Items", [.. session.Catalogs.ItemCatalog.Definitions, .. modItems], item => item.Name, modOf, session.ContentChecks.ProblemsOf, ActivatableAppearance.Of, ActivatableAppearance.Fields, ItemsTab.PlaceholderJsonOf);
        items.EvaluateAll();
        return (new ContentEditing(workspace), items);
    }

    /// <summary>An item that holds json, decoded as the session would decode a mod's record.</summary>
    private ItemDefinition Decoded(string json) => Open().Items.CreateNew(json, "Decode")!.LastGood;

    private static ContentRecordDocument<ItemDefinition> Named(ContentEditingTable<ItemDefinition> items, string name) =>
        items.Documents.Single(document => document.Original.Name == name);

    private static ContentSchema ItemSchema(ContentWorkspace workspace) => workspace.Types.GetValueCodec(typeof(ItemDefinition)).Schema;

    private static void SetField(ContentEditingTable<ItemDefinition> items, ContentWorkspace workspace, ContentRecordDocument<ItemDefinition> document, string key, JsonNode? value) =>
        items.Apply(document, new ContentInspectorEdit($"Change {key}", record => record.SetField(ContentPath.Root, ItemSchema(workspace), key, value), null, ChangesShape: false));

    private static string[] LinesOf(string path) => File.ReadAllText(path).Split('\n');

    [TestMethod]
    public void SavingNothingChanged_WritesNothing_AndPublishingAnUnchangedRecord_LeavesMasterByteIdentical()
    {
        var (editing, items) = Open();
        var before = File.ReadAllBytes(MasterItemsPath);

        Assert.AreEqual("Nothing to save.", editing.Save());
        StringAssert.StartsWith(editing.Publish(items, Named(items, "Wand of Fireball").Id), "Published");

        CollectionAssert.AreEqual(before, File.ReadAllBytes(MasterItemsPath));
        Assert.IsFalse(File.Exists(DraftItemsPath));
    }

    [TestMethod]
    public void OneChangedField_IsOneChangedLine()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        var before = LinesOf(MasterItemsPath);

        SetField(items, editing.Workspace, wand, "gv", JsonValue.Create(wand.LastGood.GoldValue + 7));
        StringAssert.StartsWith(editing.PublishAll(), "Saved 1 to master");

        var after = LinesOf(MasterItemsPath);
        Assert.HasCount(before.Length, after);
        var changed = Enumerable.Range(0, before.Length).Where(index => before[index] != after[index]).ToList();
        Assert.AreEqual(wand.MasterLine, after[Assert.ContainsSingle(changed)]);
        Assert.IsFalse(wand.IsChanged);
        Assert.IsFalse(items.HasChanges);
        Assert.IsFalse(File.Exists(DraftItemsPath));
    }

    [TestMethod]
    public void Save_WritesAChangedMasterRecordToDrafts_LeavingMasterAlone_AndPublishAll_ThenPublishesIt()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        var master = File.ReadAllBytes(MasterItemsPath);

        SetField(items, editing.Workspace, wand, "gv", JsonValue.Create(wand.LastGood.GoldValue + 7));
        StringAssert.StartsWith(editing.Save(), "Saved 1 to Drafts");

        CollectionAssert.AreEqual(master, File.ReadAllBytes(MasterItemsPath));
        Assert.IsTrue(wand.IsDraft);
        Assert.IsFalse(editing.Workspace.HasUnsavedChanges);
        Assert.IsTrue(editing.Workspace.HasUnpublishedChanges);

        StringAssert.StartsWith(editing.PublishAll(), "Saved 1 to master");
        Assert.IsFalse(wand.IsDraft);
        Assert.IsFalse(File.Exists(DraftItemsPath));
        Assert.IsFalse(editing.Workspace.HasUnpublishedChanges);
    }

    [TestMethod]
    public void ToPublish_ListsEachNewChangedDisabledAndDeletedRecord_WithWhereItStands_AndIsEmptyOncePublished()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        var potion = Named(items, "Health Potion");
        var torch = Named(items, "Scroll of Torch");
        var idol = Named(items, "Toxic Idol");
        Assert.IsEmpty(items.PendingPublish().ToList());

        SetField(items, editing.Workspace, wand, "gv", JsonValue.Create(wand.LastGood.GoldValue + 7));
        SetField(items, editing.Workspace, wand, "n", JsonValue.Create("Wand of Bigger Fireball"));
        SetField(items, editing.Workspace, potion, ContentTable.DisabledKey, JsonValue.Create(true));
        SetField(items, editing.Workspace, torch, "gv", JsonValue.Create(torch.LastGood.GoldValue));
        var created = items.CreateNew($"{{\"id\":\"{ContentGuid.Format(Guid.NewGuid())}\",\"n\":\"Brand New\",\"g\":\"n\"}}", "New")!;
        Assert.IsTrue(items.Delete(idol.Id, "Delete Toxic Idol"));

        var pending = items.PendingPublish().ToDictionary(line => line.Name);

        Assert.HasCount(4, pending, string.Join(" | ", pending.Keys));
        Assert.AreEqual("2 changes", pending["Wand of Bigger Fireball"].Change);
        Assert.AreEqual("Unsaved", pending["Wand of Bigger Fireball"].State);
        Assert.AreEqual("Disabled", pending["Health Potion"].Change);
        Assert.AreEqual("New", pending["Brand New"].Change);
        Assert.AreEqual(created.Id, pending["Brand New"].RecordId);
        Assert.AreEqual("Deleted", pending["Toxic Idol"].Change);
        Assert.AreEqual("Items", pending["Toxic Idol"].TabTitle);

        editing.Save();
        Assert.AreEqual("Saved as draft", items.PendingPublish().Single(line => line.Name == "Wand of Bigger Fireball").State);
        Assert.AreEqual("Deletion saved as draft", items.PendingPublish().Single(line => line.Name == "Toxic Idol").State);
    }

    [TestMethod]
    public void APublish_RefusesToWriteOverAMasterLineThatChangedOnDisk_UntilMasterIsReloaded()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        var openedLine = wand.MasterLine!;
        SetField(items, editing.Workspace, wand, "gv", JsonValue.Create(wand.LastGood.GoldValue + 7));

        var movedLine = openedLine.Replace("\"n\":\"Wand of Fireball\"", "\"n\":\"Wand of Fireball, edited in git\"", StringComparison.Ordinal);
        File.WriteAllText(MasterItemsPath, File.ReadAllText(MasterItemsPath).Replace(openedLine, movedLine, StringComparison.Ordinal));
        var masterOnDisk = File.ReadAllBytes(MasterItemsPath);

        StringAssert.Contains(editing.Publish(items, wand.Id), "Master changed on disk");

        CollectionAssert.AreEqual(masterOnDisk, File.ReadAllBytes(MasterItemsPath));
        Assert.IsTrue(wand.IsDraft);
        Assert.AreEqual(movedLine, wand.MasterMovedTo!.Line);
        Assert.AreEqual(openedLine, wand.MasterLine);
        StringAssert.StartsWith(items.PendingPublish().Single(line => line.RecordId == wand.Id).State, "Master changed on disk");

        Assert.IsTrue(items.ReloadMaster(wand.Id));

        Assert.AreEqual(movedLine, wand.MasterLine);
        Assert.IsNull(wand.MasterMovedTo);
        Assert.IsEmpty(wand.SaveProblems);
        var differences = wand.DifferencesFromMaster(editing.Workspace.Types)!;
        CollectionAssert.AreEquivalent(new[] { "n", "gv" }, differences.Select(difference => difference.Path.Text).ToArray());

        StringAssert.StartsWith(editing.Publish(items, wand.Id), "Published");
        Assert.AreEqual(wand.MasterLine, File.ReadAllLines(MasterItemsPath).Single(line => line.Contains(ContentGuid.Format(wand.Id), StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ADraftWithErrors_IsNotWaitingToPublish_SoPlayHasNothingToAskAboutOnceItIsSaved()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        SetField(items, editing.Workspace, wand, "n", JsonValue.Create(string.Empty));

        editing.PublishAll();

        Assert.IsTrue(wand.IsDraft);
        Assert.IsFalse(wand.IsWaitingToPublish);
        Assert.IsFalse(editing.Workspace.HasUnpublishedChanges);
        var (reopenedWorkspace, _) = Open();
        Assert.IsFalse(reopenedWorkspace.Workspace.HasUnpublishedChanges);
    }

    [TestMethod]
    public void ARecordThatDoesntDecode_IsSavedToDraftsAsWritten_AndReopensWithItsErrors()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        var master = File.ReadAllBytes(MasterItemsPath);
        Assert.IsTrue(editing.Workspace.Types.TryGetMember(typeof(Game.Modules.Actions.IActionActivator), "Scroll", out var scroll));
        items.Apply(wand, new ContentInspectorEdit("Make Activator Scroll", record => record.ChangeType(ContentPath.Root, ItemSchema(editing.Workspace), "ac", "Scroll", null, scroll.Schema), null, ChangesShape: true));
        Assert.IsNotNull(wand.DecodeFailure);
        var written = wand.Record.Json;

        StringAssert.StartsWith(editing.Save(), "Saved 1 to Drafts");

        CollectionAssert.AreEqual(master, File.ReadAllBytes(MasterItemsPath));
        Assert.Contains(written, LinesOf(DraftItemsPath));
        var (_, reopened) = Open();
        var reopenedWand = reopened.Find(wand.Id)!;
        Assert.AreEqual(written, reopenedWand.Record.Json);
        Assert.IsNotNull(reopenedWand.DecodeFailure);
        Assert.IsTrue(reopenedWand.IsDraft);
        Assert.IsTrue(reopenedWand.IsInMaster);
        Assert.AreEqual("Wand of Fireball", reopenedWand.LastGood.Name);
    }

    [TestMethod]
    public void SaveAsDraft_PutsARecordThatPasses_InDrafts_AndMasterKeepsItsLine()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        var master = File.ReadAllBytes(MasterItemsPath);
        SetField(items, editing.Workspace, wand, "gv", JsonValue.Create(wand.LastGood.GoldValue + 2));

        StringAssert.StartsWith(editing.SaveAsDraft(items, wand.Id), "Saved Wand of Fireball as a draft");
        Assert.AreEqual("Nothing to save.", editing.Save());

        CollectionAssert.AreEqual(master, File.ReadAllBytes(MasterItemsPath));
        Assert.AreEqual(wand.Record.Json, LinesOf(DraftItemsPath).Single(line => ContentTable.TryReadId(line, out var id) && id == wand.Id));
        var (reopenedWorkspace, reopened) = Open();
        Assert.IsTrue(reopened.Find(wand.Id)!.IsDraft);
        Assert.IsFalse(reopened.HasChanges);
        Assert.IsTrue(reopenedWorkspace.Workspace.HasUnpublishedChanges);
    }

    [TestMethod]
    public void ADeletedPublishedRecord_SaveAllRecordsItInDrafts_AReopenKeepsItDeleted_AndPublishAllTakesItOutOfMaster()
    {
        var (editing, items) = Open();
        var lone = items.CreateNew(ItemJson(Guid.NewGuid(), "Lone Item"), "New Lone Item")!;
        editing.PublishAll();
        var published = File.ReadAllBytes(MasterItemsPath);

        editing.Delete(items, lone.Id);
        Assert.IsTrue(editing.Workspace.HasUnsavedChanges);
        StringAssert.Contains(editing.Save(), "removed 1");

        CollectionAssert.AreEqual(published, File.ReadAllBytes(MasterItemsPath));
        Assert.Contains(ContentTableFiles.RemovalLine(lone.Id), LinesOf(DraftItemsPath));
        Assert.IsFalse(editing.Workspace.HasUnsavedChanges);
        Assert.IsTrue(editing.Workspace.HasUnpublishedChanges);

        var (reopenedWorkspace, reopened) = Open();
        Assert.IsNull(reopened.Find(lone.Id));
        Assert.IsTrue(reopened.IsDeleted(lone.Id));
        Assert.AreEqual("Lone Item", Assert.ContainsSingle(reopened.DeletedDocuments).LastGood.Name);
        Assert.IsFalse(reopenedWorkspace.Workspace.HasUnsavedChanges);
        Assert.IsTrue(reopenedWorkspace.Workspace.HasUnpublishedChanges);

        StringAssert.Contains(reopenedWorkspace.PublishAll(), "removed 1");
        Assert.IsFalse(LinesOf(MasterItemsPath).Any(line => ContentTable.TryReadId(line, out var id) && id == lone.Id));
        Assert.IsFalse(File.Exists(DraftItemsPath));
        Assert.IsFalse(reopenedWorkspace.Workspace.HasUnpublishedChanges);
        Assert.IsFalse(reopened.IsDeleted(lone.Id));
        Assert.IsEmpty(reopened.DeletedDocuments);
    }

    [TestMethod]
    public void ASavedDeletion_ReopensListedAsDeleted_AndRestoreBringsItBack_TakingTheRemovalLineOut()
    {
        var (editing, items) = Open();
        var lone = items.CreateNew(ItemJson(Guid.NewGuid(), "Lone Item"), "New Lone Item")!;
        editing.PublishAll();
        var published = File.ReadAllBytes(MasterItemsPath);
        editing.Delete(items, lone.Id);
        editing.Save();

        var (reopenedWorkspace, reopened) = Open();
        var deleted = Assert.ContainsSingle(reopened.DeletedDocuments);
        Assert.IsTrue(reopened.RestoreDeleted(deleted, "Restore Lone Item"));

        Assert.IsFalse(reopened.IsDeleted(lone.Id));
        Assert.AreSame(deleted, reopened.Find(lone.Id));
        Assert.IsTrue(reopenedWorkspace.Workspace.HasUnsavedChanges);
        reopenedWorkspace.Save();
        Assert.IsFalse(File.Exists(DraftItemsPath));
        CollectionAssert.AreEqual(published, File.ReadAllBytes(MasterItemsPath));
        Assert.IsFalse(reopenedWorkspace.Workspace.HasUnpublishedChanges);

        reopenedWorkspace.Undo(null);
        Assert.IsTrue(reopened.IsDeleted(lone.Id));
    }

    [TestMethod]
    public void AMasterRecordAModReplaces_OpensOnceAsMastersLine_EditableAndNamingTheMod()
    {
        var potionId = Open().Items.Documents.Single(document => document.Original.Name == "Health Potion").Id;

        var (_, items) = Open(item => item.Id == potionId ? "TestMod" : null, []);

        var potion = Assert.ContainsSingle(items.Documents.Where(document => document.Id == potionId));
        Assert.IsFalse(potion.IsReadOnly);
        Assert.AreEqual("TestMod", potion.ReplacedInGameBy);
        Assert.IsTrue(potion.IsInMaster);
        Assert.AreEqual(potion.MasterLine, LinesOf(MasterItemsPath).Single(line => ContentTable.TryReadId(line, out var id) && id == potionId));
        Assert.IsFalse(potion.IsChanged);
    }

    [TestMethod]
    public void AMasterRecordAModReplaces_WithADraft_OpensOnceAsTheDraft()
    {
        var (editing, items) = Open();
        var potion = Named(items, "Health Potion");
        SetField(items, editing.Workspace, potion, "gv", JsonValue.Create(potion.LastGood.GoldValue + 11));
        editing.Save();
        var draft = potion.Record.Json;

        var (reopenedWorkspace, reopened) = Open(item => item.Id == potion.Id ? "TestMod" : null, []);

        var opened = Assert.ContainsSingle(reopened.Documents.Where(document => document.Id == potion.Id));
        Assert.AreSame(opened, reopened.Find(potion.Id));
        Assert.IsFalse(opened.IsReadOnly);
        Assert.AreEqual("TestMod", opened.ReplacedInGameBy);
        Assert.AreEqual(draft, opened.Record.Json);
        Assert.IsTrue(opened.IsDraft);
        Assert.IsTrue(opened.IsInMaster);

        StringAssert.StartsWith(reopenedWorkspace.PublishAll(), "Saved 1 to master");
        Assert.Contains(opened.MasterLine!, LinesOf(MasterItemsPath));
        Assert.HasCount(1, LinesOf(MasterItemsPath).Where(line => ContentTable.TryReadId(line, out var id) && id == potion.Id));
    }

    [TestMethod]
    public void ARecordOnlyAModHas_IsReadOnly_AndNamesNoReplacement()
    {
        var modItem = Decoded(ItemJson(Guid.NewGuid(), "Mod Only Item"));

        var (_, items) = Open(item => item.Id == modItem.Id ? "TestMod" : null, [modItem]);

        var opened = Assert.ContainsSingle(items.Documents.Where(document => document.Id == modItem.Id));
        Assert.IsTrue(opened.IsReadOnly);
        Assert.IsNull(opened.ReplacedInGameBy);
        Assert.IsFalse(opened.IsInMaster);
    }

    [TestMethod]
    public void ADraftOfARecordOnlyAModHas_OpensOnceAsTheDraft_NamingTheMod()
    {
        var id = Guid.NewGuid();
        var modItem = Decoded(ItemJson(id, "Mod Only Item"));
        var draft = ItemJson(id, "Drafted Over The Mod");
        Directory.CreateDirectory(_draftsDirectory);
        File.WriteAllText(DraftItemsPath, $"{ContentTable.Header(GameContent.ItemsTable)}\n{draft}\n");

        var (_, items) = Open(item => item.Id == id ? "TestMod" : null, [modItem]);

        var opened = Assert.ContainsSingle(items.Documents.Where(document => document.Id == id));
        Assert.IsFalse(opened.IsReadOnly);
        Assert.AreEqual("TestMod", opened.ReplacedInGameBy);
        Assert.AreEqual("Drafted Over The Mod", opened.LastGood.Name);
        Assert.IsTrue(opened.IsDraft);
        Assert.IsFalse(opened.IsInMaster);
    }

    [TestMethod]
    public void ANewRecordUndone_IsNotListedAsDeleted()
    {
        var (editing, items) = Open();
        var created = items.CreateNew(ItemJson(Guid.NewGuid(), "Passing Item"), "New Passing Item")!;

        editing.Undo(null);

        Assert.IsFalse(items.IsDeleted(created.Id));
        Assert.IsEmpty(items.DeletedDocuments);
    }

    [TestMethod]
    public void APublishThatBreaksARecordItDoesntTouch_IsRefusedWhole_AndMasterIsLeftAlone()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        var potion = Named(items, "Health Potion");
        _trialOverride = files => new ContentTrialOutcome(
            files[GameContent.ItemsFileName].Contains("Breaks The Potion", StringComparison.Ordinal) ? [new ContentFailure(GameContent.ItemsFileName, 1, potion.Id, "Broken by the test.")] : [],
            null);
        var master = File.ReadAllBytes(MasterItemsPath);
        SetField(items, editing.Workspace, wand, "n", JsonValue.Create("Breaks The Potion"));

        StringAssert.StartsWith(editing.Publish(items, wand.Id), "Not published, saved as a draft");

        CollectionAssert.AreEqual(master, File.ReadAllBytes(MasterItemsPath));
        Assert.IsTrue(wand.IsDraft);
        StringAssert.Contains(Assert.ContainsSingle(wand.SaveProblems), "Broken by the test.");
    }

    [TestMethod]
    public void AFailureMasterAlreadyHas_DoesntStopAPublish()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        var potion = Named(items, "Health Potion");
        _trialOverride = _ => new ContentTrialOutcome([new ContentFailure(GameContent.ItemsFileName, 1, potion.Id, "Already broken.")], null);
        SetField(items, editing.Workspace, wand, "gv", JsonValue.Create(wand.LastGood.GoldValue + 5));

        StringAssert.StartsWith(editing.Publish(items, wand.Id), "Published");

        Assert.IsFalse(wand.IsDraft);
        Assert.Contains(wand.MasterLine!, LinesOf(MasterItemsPath));
    }

    [TestMethod]
    public void ADeletionMasterDoesntBuildWithout_StaysInMaster_SavedAsADraftWithWhy()
    {
        var (editing, items) = Open();
        var lone = items.CreateNew(ItemJson(Guid.NewGuid(), "Lone Item"), "New Lone Item")!;
        editing.PublishAll();
        var published = File.ReadAllBytes(MasterItemsPath);
        _trialOverride = _ => new ContentTrialOutcome([], "Refused by the test.");
        editing.Delete(items, lone.Id);

        StringAssert.Contains(editing.PublishAll(), "kept 1 deletion");

        CollectionAssert.AreEqual(published, File.ReadAllBytes(MasterItemsPath));
        Assert.Contains(ContentTableFiles.RemovalLine(lone.Id), LinesOf(DraftItemsPath));
        Assert.IsTrue(items.IsDeleted(lone.Id));
        StringAssert.Contains(Assert.ContainsSingle(lone.SaveProblems), "Refused by the test.");
        Assert.IsTrue(items.Problems().Any(problem => problem.RecordId == lone.Id && problem.Description.Contains("Refused by the test.", StringComparison.Ordinal)));
        Assert.IsFalse(editing.Workspace.HasUnsavedChanges);
    }

    [TestMethod]
    public void AMasterFileThatCantBeReplaced_LeavesEverythingAsItWas_AndTheRecordUnsaved()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        var master = File.ReadAllBytes(MasterItemsPath);
        SetField(items, editing.Workspace, wand, "gv", JsonValue.Create(wand.LastGood.GoldValue + 9));

        string status;
        using (new FileStream(MasterItemsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            status = editing.PublishAll();
        }

        StringAssert.StartsWith(status, "Couldn't save");
        CollectionAssert.AreEqual(master, File.ReadAllBytes(MasterItemsPath));
        Assert.IsTrue(wand.IsChanged);
        Assert.IsTrue(editing.Workspace.HasUnsavedChanges);
        Assert.IsEmpty(Directory.GetFiles(_masterDirectory, "*.tmp"));

        StringAssert.StartsWith(editing.PublishAll(), "Saved 1 to master");
        Assert.IsFalse(wand.IsChanged);
    }

    [TestMethod]
    public void AMasterFileThatCantBeRead_SavesNothing()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        SetField(items, editing.Workspace, wand, "gv", JsonValue.Create(wand.LastGood.GoldValue + 4));

        string status;
        using (new FileStream(MasterItemsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            status = editing.PublishAll();
        }

        StringAssert.StartsWith(status, "Couldn't save, so nothing was written");
        Assert.IsTrue(wand.IsChanged);
        Assert.IsFalse(File.Exists(DraftItemsPath));
    }

    [TestMethod]
    public void UndoingASavedDeletion_ThenSaveAll_TakesTheRemovalLineBackOut()
    {
        var (editing, items) = Open();
        var lone = items.CreateNew(ItemJson(Guid.NewGuid(), "Lone Item"), "New Lone Item")!;
        editing.PublishAll();
        editing.Delete(items, lone.Id);
        editing.Save();

        editing.Undo(null);
        Assert.IsTrue(editing.Workspace.HasUnsavedChanges);
        editing.Save();

        Assert.IsFalse(File.Exists(DraftItemsPath));
        Assert.IsFalse(editing.Workspace.HasUnpublishedChanges);
        Assert.IsTrue(items.Find(lone.Id)!.IsInMaster);
    }

    [TestMethod]
    public void ARecordNamingADraftOnlyItem_GoesToDrafts_AndItsErrorListLineNamesIt()
    {
        var (editing, items) = Open();
        var venom = items.CreateNew($"{{\"id\":\"{ContentGuid.Format(Guid.NewGuid())}\",\"n\":\"Venom Sample\",\"g\":\"v\"}}", "New item Venom Sample")!;
        var crate = items.CreateNew($"{{\"id\":\"{ContentGuid.Format(Guid.NewGuid())}\",\"n\":\"Venom Crate\",\"g\":\"c\",\"ct\":{{\"$\":\"Set\",\"e\":[{{\"id\":\"{ContentGuid.Format(venom.Id)}\"}}]}}}}", "New item Venom Crate")!;
        Assert.IsFalse(crate.HasErrors);
        editing.SaveAsDraft(items, venom.Id);

        editing.Publish(items, crate.Id);

        Assert.IsTrue(crate.IsDraft);
        Assert.IsFalse(crate.IsInMaster);
        StringAssert.Contains(Assert.ContainsSingle(crate.SaveProblems), "Venom Sample");
        Assert.IsTrue(items.Problems().Any(problem => problem.RecordId == crate.Id && problem.Description.Contains("Venom Sample", StringComparison.Ordinal)));
        Assert.IsFalse(LinesOf(MasterItemsPath).Any(line => ContentTable.TryReadId(line, out var id) && (id == crate.Id || id == venom.Id)));
    }

    [TestMethod]
    public void ARecordTheTrialBuildFails_GoesToDrafts_WhileTheOthersInTheSameSaveReachMaster()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        var potion = Named(items, "Health Potion");
        _trialOverride = files => new ContentTrialOutcome(
            files[GameContent.ItemsFileName].Contains("Refused Wand", StringComparison.Ordinal) ? [new ContentFailure(GameContent.ItemsFileName, 1, wand.Id, "Refused by the test.")] : [],
            null);
        SetField(items, editing.Workspace, wand, "n", JsonValue.Create("Refused Wand"));
        SetField(items, editing.Workspace, potion, "gv", JsonValue.Create(potion.LastGood.GoldValue + 3));

        StringAssert.StartsWith(editing.PublishAll(), "Saved 1 to master, 1 to Drafts");

        Assert.IsTrue(wand.IsDraft);
        StringAssert.Contains(Assert.ContainsSingle(wand.SaveProblems), "Refused by the test.");
        Assert.IsFalse(potion.IsDraft);
        Assert.Contains(potion.MasterLine!, LinesOf(MasterItemsPath));
    }

    [TestMethod]
    public void PublishingARecordWithAnError_LeavesMasterAlone_AndSavesItAsADraft()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        var master = File.ReadAllBytes(MasterItemsPath);
        SetField(items, editing.Workspace, wand, "n", JsonValue.Create(string.Empty));

        StringAssert.StartsWith(editing.Publish(items, wand.Id), "Not published, saved as a draft");

        CollectionAssert.AreEqual(master, File.ReadAllBytes(MasterItemsPath));
        Assert.IsTrue(wand.IsDraft);
        Assert.IsFalse(wand.IsChanged);
    }

    [TestMethod]
    public void AContentsEntryJustAdded_IsNotFilledInYet_UntilASave_ThenItsMissingItemIsAnError()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        items.Apply(wand, new ContentInspectorEdit("Add Contents", record => record.SetField(ContentPath.Root, ItemSchema(editing.Workspace), "ct", JsonNode.Parse("{\"$\":\"Set\",\"e\":[{}]}")), null, ChangesShape: true));

        Assert.IsNotNull(wand.DecodeFailure);
        Assert.IsTrue(wand.HasErrors);
        Assert.IsTrue(wand.HasValuesNotFilledInYet);
        Assert.IsFalse(wand.FieldProblems.Any(problem => !problem.IsWarning));
        Assert.IsFalse(items.Problems().Any(problem => problem.RecordId == wand.Id && problem.Severity == WorkshopProblemSeverity.Error));

        editing.Save();

        Assert.IsTrue(wand.IsDraft);
        Assert.AreEqual("ct.e[0].id", Assert.ContainsSingle(wand.FieldProblems.Where(problem => !problem.IsWarning)).Path.Text);
        Assert.IsTrue(items.Problems().Any(problem => problem.RecordId == wand.Id && problem.FieldPath == "ct.e[0].id"));
    }

    [TestMethod]
    public void AValueMissingAlready_IsShown_WhileOneAnEditLeftMissing_IsNotFilledInYet()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        items.Apply(wand, new ContentInspectorEdit("Add Contents", record => record.SetField(ContentPath.Root, ItemSchema(editing.Workspace), "ct", JsonNode.Parse("{\"$\":\"Set\",\"e\":[{}]}")), null, ChangesShape: true));
        editing.Save();

        items.Apply(wand, new ContentInspectorEdit("Add Entry", record => record.InsertElement(ContentPath.Root.Key("ct"), set(editing.Workspace), "e", -1, new JsonObject()), null, ChangesShape: true));

        var shown = wand.FieldProblems.Where(problem => !problem.IsWarning).Select(problem => problem.Path.Text).ToList();
        CollectionAssert.AreEqual(new[] { "ct.e[0].id" }, shown);
        Assert.Contains("ct.e[1].id", wand.NotFilledInYet);

        static ContentSchema set(ContentWorkspace workspace) =>
            workspace.Types.TryGetMember(typeof(IItemContents), "Set", out var codec) ? codec.Schema : throw new InvalidOperationException();
    }

    [TestMethod]
    public void ARecordWhoseOnlyProblemIsOneFieldError_GoesToDrafts_AndOnceFixed_ReachesMasterWithItsDraftGone()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        var master = File.ReadAllBytes(MasterItemsPath);
        SetField(items, editing.Workspace, wand, "n", JsonValue.Create(string.Empty));
        Assert.IsNull(wand.DecodeFailure);
        Assert.IsTrue(wand.HasErrors);

        editing.PublishAll();
        Assert.IsTrue(wand.IsDraft);
        CollectionAssert.AreEqual(master, File.ReadAllBytes(MasterItemsPath));

        SetField(items, editing.Workspace, wand, "n", JsonValue.Create("Wand of Bigger Fireball"));
        StringAssert.StartsWith(editing.PublishAll(), "Saved 1 to master");

        Assert.IsFalse(wand.IsDraft);
        Assert.IsFalse(File.Exists(DraftItemsPath));
        Assert.IsTrue(LinesOf(MasterItemsPath).Any(line => line.Contains("Wand of Bigger Fireball", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ANewRecord_SavesAsADraft_AndPublished_IsWrittenAtItsGuidsPlace()
    {
        var (editing, items) = Open();
        var created = items.CreateNew($"{{\"id\":\"{ContentGuid.Format(Guid.NewGuid())}\",\"n\":\"Placed Item\",\"g\":\"p\"}}", "New item Placed Item")!;

        editing.Save();
        Assert.IsTrue(created.IsDraft);
        StringAssert.StartsWith(editing.Publish(items, created.Id), "Published");

        var ids = LinesOf(MasterItemsPath).Skip(1).Where(line => line.Length > 0).Select(line => ContentTable.TryReadId(line, out var id) ? id : Guid.Empty).ToList();
        Assert.Contains(created.Id, ids);
        CollectionAssert.AreEqual(ids.Order(ContentGuid.FileOrder).ToList(), ids);
        Assert.IsFalse(File.Exists(DraftItemsPath));
    }

    [TestMethod]
    public void AMasterRecordCodeBuildsWith_HasDependents_SoDeleteIsRefused()
    {
        var (editing, items) = Open();

        var dependents = editing.Workspace.UsedByOf(GameContent.ItemsTable, Named(items, "Health Potion").Id);

        Assert.IsTrue(dependents.Any(dependent => dependent.Kind == "Blueprint" && dependent.Relationship.Contains("code", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void DeletingAMasterRecordNothingNames_TakesOutItsLineAlone_AndUndoBringsItBack()
    {
        var (editing, items) = Open();
        var deletable = items.Documents.First(document => document.IsInMaster && editing.Workspace.UsedByOf(GameContent.ItemsTable, document.Id).Count == 0);
        var before = LinesOf(MasterItemsPath);

        editing.Delete(items, deletable.Id);
        StringAssert.Contains(editing.Save(), "removed 1");
        CollectionAssert.AreEqual(before, LinesOf(MasterItemsPath));
        StringAssert.Contains(editing.PublishAll(), "removed 1");

        var after = LinesOf(MasterItemsPath);
        CollectionAssert.AreEqual(before.Where(line => line != deletable.MasterLine && !(ContentTable.TryReadId(line, out var id) && id == deletable.Id)).ToArray(), after);

        editing.Undo(null);
        Assert.IsNotNull(items.Find(deletable.Id));
        Assert.IsTrue(items.HasChanges);
    }

    private static string ItemJson(Guid id, string name, Guid? contains = null) =>
        contains is { } contained
            ? $"{{\"id\":\"{ContentGuid.Format(id)}\",\"n\":\"{name}\",\"g\":\"x\",\"ct\":{{\"$\":\"Set\",\"e\":[{{\"id\":\"{ContentGuid.Format(contained)}\"}}]}}}}"
            : $"{{\"id\":\"{ContentGuid.Format(id)}\",\"n\":\"{name}\",\"g\":\"x\"}}";

    /// <summary>Three published items, each holding the one before: Base, then Middle holding Base, then Top holding Middle.</summary>
    private static (ContentRecordDocument<ItemDefinition> Base, ContentRecordDocument<ItemDefinition> Middle, ContentRecordDocument<ItemDefinition> Top) PublishChain(ContentEditing editing, ContentEditingTable<ItemDefinition> items)
    {
        var bottom = items.CreateNew(ItemJson(Guid.NewGuid(), "Chain Base"), "New Chain Base")!;
        var middle = items.CreateNew(ItemJson(Guid.NewGuid(), "Chain Middle", bottom.Id), "New Chain Middle")!;
        var top = items.CreateNew(ItemJson(Guid.NewGuid(), "Chain Top", middle.Id), "New Chain Top")!;
        StringAssert.StartsWith(editing.PublishAll(), "Saved 3 to master");
        return (bottom, middle, top);
    }

    [TestMethod]
    public void AnItemsUses_ListWhatItsContentsNameAsEdited_OpeningEachInTheItemsTab()
    {
        var (editing, items) = Open();
        var held = items.CreateNew(ItemJson(Guid.NewGuid(), "Held Thing"), "New Held Thing")!;
        var holder = items.CreateNew(ItemJson(Guid.NewGuid(), "Thing Holder", held.Id), "New Thing Holder")!;

        var use = Assert.ContainsSingle(editing.Workspace.UsesOf(items, holder.Id));

        Assert.AreEqual("Held Thing", use.Name);
        Assert.AreEqual("item", use.Kind);
        Assert.AreEqual(held.Id, use.RecordId);
        Assert.AreEqual("Items", use.TabTitle);
        Assert.AreEqual(holder.Id, Assert.ContainsSingle(editing.Workspace.UsedByOf(GameContent.ItemsTable, held.Id)).RecordId);
    }

    [TestMethod]
    public void DeletingAPublishedRecordSomethingNames_IsRefusedByTheWorkspace_ChangingNothing()
    {
        var (editing, items) = Open();
        var potion = Named(items, "Health Potion");

        Assert.IsNotNull(editing.Workspace.DeleteRefusal(items, potion.Id));
        StringAssert.StartsWith(editing.Delete(items, potion.Id), "Can't delete Health Potion");

        Assert.AreSame(potion, items.Find(potion.Id));
        Assert.IsFalse(editing.Workspace.Log.CanUndo);
        Assert.IsFalse(items.HasChanges);
    }

    [TestMethod]
    public void DisablingADraft_IsRefusedByTheWorkspace_ChangingNothing()
    {
        var (editing, items) = Open();
        var draft = items.CreateNew(ItemJson(Guid.NewGuid(), "Draft Item"), "New Draft Item")!;
        var json = draft.Record.Json;

        StringAssert.StartsWith(editing.Disable(items, draft.Id, replacement: null), "Not disabled: Only a published item");

        Assert.AreEqual(json, draft.Record.Json);
        Assert.IsFalse(draft.IsDisabled);
    }

    [TestMethod]
    public void APublish_WritesTheMasterItTried_EvenWhenTheFileChangesOnDiskMeanwhile()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        SetField(items, editing.Workspace, wand, "gv", JsonValue.Create(wand.LastGood.GoldValue + 3));
        string? tried = null;
        _trialOverride = files =>
        {
            tried = files[GameContent.ItemsFileName];
            File.AppendAllText(MasterItemsPath, "written meanwhile\n");
            return new ContentTrialOutcome([], null);
        };

        StringAssert.StartsWith(editing.PublishAll(), "Saved 1 to master");

        Assert.AreEqual(tried, File.ReadAllText(MasterItemsPath));
    }

    [TestMethod]
    public void DisablingARecordNothingNames_WritesOffAndNothingElse_AndEnablingPutsItsLineBackByteForByte()
    {
        var (editing, items) = Open();
        var lone = items.CreateNew(ItemJson(Guid.NewGuid(), "Lone Item"), "New Lone Item")!;
        editing.PublishAll();
        var published = LinesOf(MasterItemsPath);
        var line = lone.MasterLine!;

        Assert.IsEmpty(editing.Workspace.PlanDisable(items, lone.Id).Direct);
        editing.Disable(items, lone.Id, replacement: null);
        editing.PublishAll();

        var disabled = LinesOf(MasterItemsPath);
        var changed = Assert.ContainsSingle(Enumerable.Range(0, published.Length).Where(index => published[index] != disabled[index]));
        Assert.AreEqual(line[..^1] + ",\"off\":true}", disabled[changed]);

        editing.Enable(items, lone.Id);
        editing.PublishAll();
        CollectionAssert.AreEqual(published, LinesOf(MasterItemsPath));
    }

    [TestMethod]
    public void CascadeOverAThreeDeepChain_DisablesAllOfIt_AndOneUndoBringsAllOfItBack()
    {
        var (editing, items) = Open();
        var (bottom, middle, top) = PublishChain(editing, items);

        var plan = editing.Workspace.PlanDisable(items, bottom.Id);
        Assert.IsNull(plan.CascadeRefusal);
        CollectionAssert.AreEquivalent(new[] { middle.Id, top.Id }, plan.Cascaded.Select(dependent => dependent.Id).ToArray());

        StringAssert.StartsWith(editing.DisableWithCascade(items, bottom.Id), "Disabled Chain Base and 2 more");
        Assert.IsTrue(bottom.IsDisabled && middle.IsDisabled && top.IsDisabled);
        Assert.IsFalse(top.HasErrors);

        StringAssert.StartsWith(editing.Undo(null), "Undid Disable Chain Base and what names it: 3 items");
        Assert.IsFalse(bottom.IsDisabled || middle.IsDisabled || top.IsDisabled);
    }

    [TestMethod]
    public void AnEnabledRecordNamingADisabledOneWithNoReplacement_IsAnErrorAtTheReference_AndAReplacementClearsIt()
    {
        var (editing, items) = Open();
        var (bottom, middle, _) = PublishChain(editing, items);
        var stand = items.CreateNew(ItemJson(Guid.NewGuid(), "Stand In"), "New Stand In")!;
        editing.PublishAll();

        editing.Disable(items, bottom.Id, replacement: null);

        var problem = Assert.ContainsSingle(middle.ReferenceProblems);
        Assert.AreEqual("ct.e[0].id", problem.Path.Text);
        Assert.IsTrue(middle.HasErrors);

        editing.Undo(null);
        editing.Disable(items, bottom.Id, stand.Id);
        Assert.IsEmpty(middle.ReferenceProblems);
    }

    [TestMethod]
    public void AReplacementChainThatComesBackRound_IsRefusedAtTheReplacement()
    {
        var (editing, items) = Open();
        var first = items.CreateNew(ItemJson(Guid.NewGuid(), "Cycle One"), "New Cycle One")!;
        var second = items.CreateNew(ItemJson(Guid.NewGuid(), "Cycle Two"), "New Cycle Two")!;
        editing.PublishAll();

        editing.Disable(items, first.Id, second.Id);
        editing.Disable(items, second.Id, first.Id);

        Assert.AreEqual("rep", Assert.ContainsSingle(second.ReferenceProblems).Path.Text);
        StringAssert.Contains(second.ReferenceProblems[0].Message, "come back round");
        Assert.AreEqual("rep", Assert.ContainsSingle(first.ReferenceProblems).Path.Text);
    }

    [TestMethod]
    public void ADraftDependent_NeitherBlocksNorIsCascaded_WhileCodeThatNamesARecordRefusesCascade()
    {
        var (editing, items) = Open();
        var target = items.CreateNew(ItemJson(Guid.NewGuid(), "Draft Target"), "New Draft Target")!;
        editing.PublishAll();
        var draft = items.CreateNew(ItemJson(Guid.NewGuid(), "Draft Holder", target.Id), "New Draft Holder")!;
        editing.Save();

        var plan = editing.Workspace.PlanDisable(items, target.Id);
        Assert.AreEqual(draft.Id, Assert.ContainsSingle(plan.Drafts).Id);
        Assert.IsEmpty(plan.Cascaded);
        Assert.IsNull(plan.CascadeRefusal);

        var potionPlan = editing.Workspace.PlanDisable(items, Named(items, "Health Potion").Id);
        Assert.IsNotNull(potionPlan.CascadeRefusal);
        StringAssert.StartsWith(editing.DisableWithCascade(items, Named(items, "Health Potion").Id), "Not disabled");
        Assert.IsFalse(Named(items, "Health Potion").IsDisabled);
    }

    [TestMethod]
    public void APublishedReplacement_IsWhatTheGameGets_ForTheDisabledRecordsGuid()
    {
        var (editing, items) = Open();
        var (bottom, _, _) = PublishChain(editing, items);
        var stand = items.CreateNew(ItemJson(Guid.NewGuid(), "Stand In"), "New Stand In")!;
        editing.PublishAll();
        editing.Disable(items, bottom.Id, stand.Id);
        StringAssert.StartsWith(editing.PublishAll(), "Saved 1 to master");

        var layers = GameContent.MasterOnly(_masterDirectory);
        var session = ContentSession.Build(ValidatedMods.None, layers, [], TextWriter.Null);

        Assert.IsEmpty(session.ContentFailures);
        Assert.AreEqual("Stand In", session.Catalogs.ItemCatalog.Get(bottom.Id).Name);
    }

    [TestMethod]
    public void ARebuild_KeepsUnsavedEditsOfOtherRecords_AndTheUndoLog()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        var potion = Named(items, "Health Potion");
        SetField(items, editing.Workspace, potion, "gv", JsonValue.Create(potion.LastGood.GoldValue + 1));
        var potionJson = potion.Record.Json;
        SetField(items, editing.Workspace, wand, "gv", JsonValue.Create(wand.LastGood.GoldValue + 1));
        editing.Publish(items, wand.Id);
        Assert.IsTrue(editing.Workspace.IsRebuilding);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (editing.Workspace.SessionVersion == 0 && DateTime.UtcNow < deadline)
        {
            editing.Update();
            Thread.Sleep(10);
        }

        Assert.AreEqual(1, editing.Workspace.SessionVersion);
        Assert.AreSame(potion, items.Find(potion.Id));
        Assert.AreEqual(potionJson, potion.Record.Json);
        Assert.IsTrue(potion.IsChanged);
        Assert.IsTrue(editing.Workspace.Log.CanUndo);
    }
}
