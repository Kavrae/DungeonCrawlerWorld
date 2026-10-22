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
    private static readonly Lazy<GameSession> Session = new(() => ContentSession.Build(ContentSession.ValidateMods(Path.Combine(AppContext.BaseDirectory, "NoMods"), []), [], TextWriter.Null));
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
    public void AScroll_LinksToItsSpellInTheActionsTab()
    {
        var scroll = Session.Value.Catalogs.ItemCatalog.Definitions.First(item => item.Activator is ScrollActivator);
        var spell = Session.Value.Catalogs.ActionCatalog.Definitions.Single(action => action.Id == ((ScrollActivator)scroll.Activator!).SpellId);

        var uses = ContentLinks.For(Index.Value, ContentRecordKind.Item, scroll.Id).Single(section => section.Heading == ContentLinks.UsesHeading);

        var link = uses.Links.Single(link => link.TargetId == spell.Id);
        Assert.AreEqual(ActionsTab.TabTitle, link.TargetTabTitle);
        Assert.AreEqual($"Action: {spell.Name} (casts)", link.Text);
    }

    [TestMethod]
    public void TheSpell_LinksBackToTheScroll_UnderUsedBy()
    {
        var scroll = Session.Value.Catalogs.ItemCatalog.Definitions.First(item => item.Activator is ScrollActivator);
        var spellId = ((ScrollActivator)scroll.Activator!).SpellId;

        var usedBy = ContentLinks.For(Index.Value, ContentRecordKind.Action, spellId).Single(section => section.Heading == ContentLinks.UsedByHeading);

        var link = usedBy.Links.Single(link => link.TargetId == scroll.Id);
        Assert.AreEqual(ItemsTab.TabTitle, link.TargetTabTitle);
        Assert.AreEqual($"Item: {scroll.Name} (casts it)", link.Text);
    }

    [TestMethod]
    public void TheSpellsDependents_ListTheScrollThatCastsIt_OpeningItInTheItemsTab()
    {
        var scroll = Session.Value.Catalogs.ItemCatalog.Definitions.First(item => item.Activator is ScrollActivator);
        var spellId = ((ScrollActivator)scroll.Activator!).SpellId;

        var dependent = ContentLinks.DependentsOf(Index.Value, ContentRecordKind.Action, spellId).Single(dependent => dependent.RecordId == scroll.Id);

        Assert.AreEqual(scroll.Name, dependent.Name);
        Assert.AreEqual(nameof(ContentRecordKind.Item), dependent.Kind);
        Assert.AreEqual("casts it", dependent.Relationship);
        Assert.AreEqual(ItemsTab.TabTitle, dependent.TabTitle);
    }

    [TestMethod]
    public void AnAurasDependents_IncludeWhatNoTabShows_WithNothingToOpen()
    {
        var terrainReference = Index.Value.UsedBy(ContentRecordKind.Aura, BuiltInTerrain.LavaAura.Id).First(reference => reference.From.Kind == ContentRecordKind.Terrain);

        var dependent = ContentLinks.DependentsOf(Index.Value, ContentRecordKind.Aura, BuiltInTerrain.LavaAura.Id).Single(dependent => dependent.Name == terrainReference.From.Name);

        Assert.AreEqual("radiates it", dependent.Relationship);
        Assert.IsNull(dependent.TabTitle);
        Assert.IsNull(dependent.RecordId);
    }

    [TestMethod]
    public void EveryRecord_HasUsesThenUsedBy()
    {
        var sections = ContentLinks.For(Index.Value, ContentRecordKind.Action, Guid.NewGuid());

        CollectionAssert.AreEqual(new[] { ContentLinks.UsesHeading, ContentLinks.UsedByHeading }, sections.Select(section => section.Heading).ToArray());
        Assert.IsTrue(sections.All(section => section.Links.Count == 0));
    }
}
