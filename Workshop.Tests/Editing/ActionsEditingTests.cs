using System.Text.Json.Nodes;
using Builders.Actions;
using Builders.Editing;
using DungeonCrawlerWorld.Workshop;
using Engine.Content;
using Game.Bootstrap;
using Game.Content;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Workshop.Editing;

namespace Workshop.Tests.Editing;

/// <summary>Every built-in action as the Actions tab holds it, beside the Items tab's table: read into a tree that decodes back to its own line, checked, and edited -- a scroll following its spell's edit before anything is saved.</summary>
[TestClass]
public sealed class ActionsEditingTests
{
    private static readonly Lazy<GameSession> Session = new(() => ContentSession.Build(ContentSession.ValidateMods(Path.Combine(AppContext.BaseDirectory, "NoMods"), WorkshopTestContent.Master, []), WorkshopTestContent.Master, [], TextWriter.Null));

    /// <summary>The two tables as the Workshop opens them: actions first, so a scroll decodes against the actions held.</summary>
    private static (ContentWorkspace Workspace, ContentEditingTable<ActionDefinition> Actions, ContentEditingTable<ItemDefinition> Items) Open()
    {
        var session = Session.Value;
        var workspace = new ContentWorkspace(session);
        var actions = new ContentEditingTable<ActionDefinition>(workspace, GameContent.ActionsTable, ActionsTab.TabTitle, session.Catalogs.ActionCatalog.Definitions, action => action.Name, _ => null, session.ContentChecks.ProblemsOf, ActivatableAppearance.Of, ActivatableAppearance.Fields, ActionsTab.PlaceholderJsonOf);
        var items = new ContentEditingTable<ItemDefinition>(workspace, GameContent.ItemsTable, "Items", session.Catalogs.ItemCatalog.Definitions, item => item.Name, _ => null, session.ContentChecks.ProblemsOf, ActivatableAppearance.Of, ActivatableAppearance.Fields);
        actions.EvaluateAll();
        items.EvaluateAll();
        return (workspace, actions, items);
    }

    private static ContentRecordDocument<T> Named<T>(ContentEditingTable<T> table, string name)
        where T : ActivatableDefinition =>
        table.Documents.Single(document => document.Original.Name == name);

    private static ContentSchema SchemaOf(Type type) => Session.Value.Catalogs.ContentTypes.GetValueCodec(type).Schema;

    [TestMethod]
    public void EveryBuiltInAction_OpensAsAMasterRecord_WritesBackToItsOwnLine_AndHasNoError()
    {
        var (workspace, actions, _) = Open();
        var master = File.ReadAllLines(Path.Combine(GameContent.DefaultMasterDirectory, GameContent.ActionsFileName)).Skip(1)
            .Where(line => line.Length > 0)
            .ToDictionary(line => ContentTable.TryReadId(line, out var id) ? id : Guid.Empty);

        Assert.HasCount(master.Count, actions.Documents, "The table opens exactly the master records: nothing the session's catalog holds beside them.");
        foreach (var document in actions.Documents)
        {
            Assert.IsTrue(document.IsInMaster, document.Original.Name);
            Assert.IsNull(document.DecodeFailure, document.Original.Name);
            Assert.AreEqual(master[document.Id], ContentTable.WriteLine(document.LastGood, workspace.Types), document.Original.Name);
            Assert.IsFalse(document.HasErrors, $"{document.Original.Name}: {string.Join("; ", document.CheckProblems)}");
        }
    }

    [TestMethod]
    public void ANewAction_DecodesWithNoProblem_AsAnAbilityOnTheCaster()
    {
        var (_, actions, _) = Open();
        var json = new JsonObject
        {
            [ContentTable.IdKey] = ContentGuid.Format(Guid.NewGuid()),
            [ActivatableContent.NameKey] = "New action",
            [ActivatableContent.GlyphKey] = ActivatablePlaceholder.Glyph,
            [ActivatableContent.ActivatorKey] = ActivatablePlaceholder.SelfActivator(),
        }.ToJsonString();

        var created = actions.CreateNew(json, "New action");

        Assert.IsNotNull(created);
        Assert.IsFalse(created.HasErrors, string.Join("; ", created.CheckProblems) + created.DecodeFailure);
        Assert.AreEqual("Abilities", ActionBrowserSections.HeadingOf(created.LastGood));
        Assert.IsTrue(created.LastGood.Activator.Targeting.IsSelfOnly);
    }

    [TestMethod]
    public void AnActionsStandIn_Decodes_KeepingItsNameAndGlyph()
    {
        var (_, actions, _) = Open();
        var broken = EditableContentRecord.CreateNew(GameContent.ActionsTable, typeof(ActionDefinition), $"{{\"id\":\"{ContentGuid.Format(Guid.NewGuid())}\",\"n\":\"Broken\",\"g\":\"b\",\"zz\":1}}");

        var standIn = actions.Decode(ActionsTab.PlaceholderJsonOf(broken), out var failure);

        Assert.IsNotNull(standIn, failure);
        Assert.AreEqual(("Broken", "b"), (standIn.Name, standIn.Glyph));
    }

    [TestMethod]
    [DataRow("Self, Circle", "its shape must be Self alone: Self, Circle adds Circle, which would do nothing")]
    [DataRow("Circle", "its shape must be Self alone, not Circle")]
    public void AToggleActionWithAShapeBesideSelf_IsRefused_SayingWhatItAdds_AtItsShape(string shape, string expected)
    {
        var (_, actions, _) = Open();
        var lantern = Named(actions, "Lantern");
        var targetingSchema = SchemaOf(typeof(TargetingSpec));

        actions.Apply(lantern, new ContentInspectorEdit("Change Shape", record => record.SetField(ContentPath.Root.Key(ActivatableContent.ActivatorKey).Key("tg"), targetingSchema, "sh", JsonValue.Create(shape)), "ac.tg.sh", ChangesShape: false));

        Assert.IsTrue(lantern.HasErrors);
        var problem = lantern.CheckProblems.Single(each => each.Contains(expected, StringComparison.Ordinal));
        Assert.AreEqual("ac.tg.sh", ContentProblemLocator.Locate(lantern.Record, Session.Value.Catalogs.ContentTypes, problem)?.Text, "The Error List opens the shape.");
    }

    [TestMethod]
    public void EditingASpell_ShowsOnTheScrollOfIt_BeforeAnythingIsSaved_AndUndoPutsItBack()
    {
        var (workspace, actions, items) = Open();
        var heal = Named(actions, "Heal");
        var scroll = Named(items, "Scroll of Healing");
        Assert.IsTrue(workspace.Types.TryGetMember(typeof(Game.Effects.IEffectEntry), nameof(DirectHeal), out var healCodec));
        var healSchema = healCodec.Schema;
        var healPath = ContentPath.Root.Key(ActivatableContent.EffectsKey).Element(0).Key("e").Element(0);
        Assert.AreEqual(0.2f, HealPercentOf(scroll.LastGood), "Precondition: the scroll casts Heal as master has it.");

        actions.Apply(heal, new ContentInspectorEdit("Change Heal", record => record.SetField(healPath, healSchema, "pct", JsonValue.Create(0.3)), "fx[0].e[0].pct", ChangesShape: false));

        Assert.AreEqual(0.3f, HealPercentOf(scroll.LastGood), 0.0001f);
        Assert.IsFalse(scroll.IsChanged, "The scroll's own record is untouched: it names only its spell.");

        workspace.Undo();

        Assert.AreEqual(0.2f, HealPercentOf(scroll.LastGood), 0.0001f);
    }

    [TestMethod]
    public void ACostNamesWhatTheActivationEffectsTake_AsDeclared()
    {
        var (_, actions, _) = Open();

        Assert.AreEqual(new ActivationCostTotals(2f, 0f), ActivationCosts.Declared(Named(actions, "Heal").LastGood));
        Assert.IsTrue(ActivationCosts.Declared(Named(actions, "Quick Attack").LastGood).IsFree);
    }

    private static float HealPercentOf(ItemDefinition scroll) => ((DirectHeal)scroll.Effects[0].Entries[0]).PercentOfMaxHealth;
}
