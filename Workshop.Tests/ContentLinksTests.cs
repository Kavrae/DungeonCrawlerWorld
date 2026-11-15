using Builders.Actions;
using Builders.Auras;
using Builders.Items;
using Builders.References;
using DungeonCrawlerWorld.Workshop;
using Game.Bootstrap;
using Game.Modules.Actions.Activators;
using Game.Terrain;

namespace Workshop.Tests;

[TestClass]
public sealed class ContentLinksTests
{
    private static readonly Lazy<GameSession> Session = new(() => ContentSession.Build(ContentSession.ValidateMods(Path.Combine(AppContext.BaseDirectory, "NoMods"), WorkshopTestContent.Master, []), WorkshopTestContent.Master, [], TextWriter.Null));
    private static readonly Lazy<ContentReferenceIndex> Index = new(() => new ContentReferenceIndex(Session.Value.Catalogs));

    [TestMethod]
    public void EveryKindWithATab_OpensThatTab_AndTerrainAndBlueprintsOpenNone()
    {
        Assert.AreEqual(ActionsTab.TabTitle, ContentLinks.TabTitleFor(ContentRecordKind.Action));
        Assert.AreEqual(ItemsTab.TabTitle, ContentLinks.TabTitleFor(ContentRecordKind.Item));
        Assert.AreEqual(AurasTab.TabTitle, ContentLinks.TabTitleFor(ContentRecordKind.Aura));
        Assert.IsNull(ContentLinks.TabTitleFor(ContentRecordKind.Terrain));
        Assert.IsNull(ContentLinks.TabTitleFor(ContentRecordKind.Blueprint));
    }

    [TestMethod]
    public void AScrollsUses_ListItsSpell_OpeningItInTheActionsTab()
    {
        var scroll = Session.Value.Catalogs.ItemCatalog.Definitions.First(item => item.Activator is ScrollActivator && Session.Value.Catalogs.ActionCatalog.TryGet(((ScrollActivator)item.Activator!).SpellId, out _));
        var spell = Session.Value.Catalogs.ActionCatalog.Definitions.Single(action => action.Id == ((ScrollActivator)scroll.Activator!).SpellId);

        var use = ContentLinks.UsesOf(Index.Value, ContentRecordKind.Item, scroll.Id).Single(use => use.RecordId == spell.Id);

        Assert.AreEqual(spell.Name, use.Name);
        Assert.AreEqual(nameof(ContentRecordKind.Action), use.Kind);
        Assert.AreEqual("casts", use.Relationship);
        Assert.AreEqual(ActionsTab.TabTitle, use.TabTitle);
    }

    [TestMethod]
    public void TheSpellsUsedBy_ListTheScrollThatCastsIt_OpeningItInTheItemsTab()
    {
        var scroll = Session.Value.Catalogs.ItemCatalog.Definitions.First(item => item.Activator is ScrollActivator);
        var spellId = ((ScrollActivator)scroll.Activator!).SpellId;

        var dependent = ContentLinks.UsedByOf(Index.Value, ContentRecordKind.Action, spellId).Single(dependent => dependent.RecordId == scroll.Id);

        Assert.AreEqual(scroll.Name, dependent.Name);
        Assert.AreEqual(nameof(ContentRecordKind.Item), dependent.Kind);
        Assert.AreEqual("casts it", dependent.Relationship);
        Assert.AreEqual(ItemsTab.TabTitle, dependent.TabTitle);
    }

    [TestMethod]
    public void AnAurasUsedBy_IncludeWhatNoTabShows_WithNothingToOpen()
    {
        var terrainReference = Index.Value.UsedBy(ContentRecordKind.Aura, Game.Modules.Auras.BuiltInAuraIds.Lava).First(reference => reference.From.Kind == ContentRecordKind.Terrain);

        var dependent = ContentLinks.UsedByOf(Index.Value, ContentRecordKind.Aura, Game.Modules.Auras.BuiltInAuraIds.Lava).Single(dependent => dependent.Name == terrainReference.From.Name);

        Assert.AreEqual("radiates it", dependent.Relationship);
        Assert.IsNull(dependent.TabTitle);
        Assert.IsNull(dependent.RecordId);
    }

    [TestMethod]
    public void ARecordNothingNames_AndThatNamesNothing_HasNoLines()
    {
        Assert.IsEmpty(ContentLinks.UsesOf(Index.Value, ContentRecordKind.Action, Guid.NewGuid()));
        Assert.IsEmpty(ContentLinks.UsedByOf(Index.Value, ContentRecordKind.Action, Guid.NewGuid()));
    }
}
