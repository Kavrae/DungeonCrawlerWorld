using Builders.Actions;
using Builders.Editing;
using Builders.Items;
using DungeonCrawlerWorld.Workshop;
using Engine.Content;
using Game.Bootstrap;
using Game.Content;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;
using Workshop.Editing;

namespace Workshop.Tests.Editing;

/// <summary>Disabling across the Actions and Items tables, saved into a copy of master: a spell and a scroll of it disabled together and brought back as one step, a replacement the scroll then casts, and what code names refusing Cascade.</summary>
[TestClass]
public sealed class CrossTableDisableTests
{
    private static readonly Lazy<GameSession> Session = new(() => ContentSession.Build(ContentSession.ValidateMods(Path.Combine(AppContext.BaseDirectory, "NoMods"), WorkshopTestContent.Master, []), WorkshopTestContent.Master, [], TextWriter.Null));

    private string _root = null!;
    private string _masterDirectory = null!;
    private string _draftsDirectory = null!;

    [TestInitialize]
    public void CopyMaster()
    {
        _root = Path.Combine(Path.GetTempPath(), "WorkshopCrossTableDisable", Guid.NewGuid().ToString("N"));
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

    /// <summary>Both tables as the Workshop opens them, actions first, saving into the copy.</summary>
    private (ContentEditing Editing, ContentEditingTable<ActionDefinition> Actions, ContentEditingTable<ItemDefinition> Items) Open()
    {
        var saving = new ContentSaving(
            table => new ContentTableFiles(table, Path.Combine(_masterDirectory, GameContent.FileNameOf(table)), Path.Combine(_draftsDirectory, GameContent.FileNameOf(table))),
            () => ContentSession.ReadMasterFiles(_masterDirectory),
            files => ContentSession.TrialBuild(files, []),
            () => Session.Value);
        var session = Session.Value;
        var workspace = new ContentWorkspace(session, saving);
        var actions = new ContentEditingTable<ActionDefinition>(workspace, GameContent.ActionsTable, ActionsTab.TabTitle, session.Catalogs.ActionCatalog.Definitions, action => action.Name, _ => null, session.ContentChecks.ProblemsOf, ActivatableAppearance.Of, ActivatableAppearance.Fields, ActionsTab.PlaceholderJsonOf);
        var items = new ContentEditingTable<ItemDefinition>(workspace, GameContent.ItemsTable, ItemsTab.TabTitle, session.Catalogs.ItemCatalog.Definitions, item => item.Name, _ => null, session.ContentChecks.ProblemsOf, ActivatableAppearance.Of, ActivatableAppearance.Fields, ItemsTab.PlaceholderJsonOf);
        actions.EvaluateAll();
        items.EvaluateAll();
        return (new ContentEditing(workspace), actions, items);
    }

    private static ContentRecordDocument<ActionDefinition> NamedAction(ContentEditingTable<ActionDefinition> actions, string name) =>
        actions.Documents.Single(document => document.Original.Name == name);

    /// <summary>Test Bolt (a spell nothing in code names) and a scroll of it, both published.</summary>
    private static (ContentRecordDocument<ActionDefinition> Bolt, ContentRecordDocument<ItemDefinition> Scroll) PublishBoltAndScroll(ContentEditing editing, ContentEditingTable<ActionDefinition> actions, ContentEditingTable<ItemDefinition> items)
    {
        var boltId = Guid.NewGuid();
        var bolt = actions.CreateNew($"{{\"id\":\"{ContentGuid.Format(boltId)}\",\"n\":\"Test Bolt\",\"g\":\"b\",\"fx\":[{{\"e\":[{{\"$\":\"DirectDamage\",\"mn\":3,\"mx\":3}}]}}],\"ac\":{{\"$\":\"Spell\",\"tg\":{{\"sh\":\"SingleTarget\",\"r\":5}},\"tm\":{{}}}}}}", "New Test Bolt")!;
        var scroll = items.CreateNew($"{{\"id\":\"{ContentGuid.Format(Guid.NewGuid())}\",\"n\":\"Scroll of Test Bolt\",\"g\":\"s\",\"ac\":{{\"$\":\"Scroll\",\"sp\":\"{ContentGuid.Format(boltId)}\"}}}}", "New Scroll of Test Bolt")!;
        StringAssert.StartsWith(editing.PublishAll(), "Saved 2 to master");
        return (bolt, scroll);
    }

    [TestMethod]
    public void ASpellAScrollCasts_CantBeDeleted_AndCascadeDisablesBoth_AsOneStepUndoneAtOnce()
    {
        var (editing, actions, items) = Open();
        var (bolt, scroll) = PublishBoltAndScroll(editing, actions, items);

        StringAssert.Contains(editing.Workspace.DeleteRefusal(actions, bolt.Id), "Scroll of Test Bolt");
        var plan = editing.Workspace.PlanDisable(actions, bolt.Id);
        Assert.IsNull(plan.CascadeRefusal);
        Assert.AreEqual(scroll.Id, Assert.ContainsSingle(plan.Cascaded).Id);

        StringAssert.StartsWith(editing.DisableWithCascade(actions, bolt.Id), "Disabled Test Bolt and 1 more");
        Assert.IsTrue(bolt.IsDisabled && scroll.IsDisabled);

        var undone = editing.Undo(null);
        StringAssert.Contains(undone, "1 action");
        StringAssert.Contains(undone, "1 item");
        Assert.IsFalse(bolt.IsDisabled || scroll.IsDisabled);
    }

    [TestMethod]
    public void ASpellDisabledWithAReplacement_IsWhatItsScrollThenCasts_AndTheScrollsLineDoesntChange()
    {
        var (editing, actions, items) = Open();
        var (bolt, scroll) = PublishBoltAndScroll(editing, actions, items);
        var scrollLine = scroll.MasterLine;
        var magicMissile = NamedAction(actions, "Magic Missile");

        editing.Disable(actions, bolt.Id, magicMissile.Id);

        Assert.AreSame(magicMissile.LastGood, ((ScrollActivator)scroll.LastGood.Activator!).Spell, "The scroll follows the replacement before anything is saved.");
        Assert.IsEmpty(scroll.ReferenceProblems);

        StringAssert.StartsWith(editing.PublishAll(), "Saved 1 to master");
        Assert.AreEqual(scrollLine, scroll.MasterLine, "The scroll names its spell's Guid; only the spell's line changes.");
    }

    [TestMethod]
    public void DisablingDodge_ListsTheRacesThatGrantItAsCode_AndRefusesCascade()
    {
        var (editing, actions, _) = Open();
        var dodge = NamedAction(actions, "Dodge");

        var plan = editing.Workspace.PlanDisable(actions, dodge.Id);

        Assert.IsNotNull(plan.CascadeRefusal);
        foreach (var race in new[] { "Human", "Goblin", "Fairy", "Ghost" })
        {
            Assert.IsTrue(plan.NotEditable.Any(dependent => dependent.Name.StartsWith(race, StringComparison.Ordinal)), $"{race} grants Dodge in code.");
        }

        StringAssert.StartsWith(editing.DisableWithCascade(actions, dodge.Id), "Not disabled");
        Assert.IsFalse(dodge.IsDisabled);
    }
}
