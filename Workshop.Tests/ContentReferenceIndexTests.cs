using Builders.References;
using DungeonCrawlerWorld.Workshop;
using Game.Bootstrap;
using Game.Effects;
using Game.Modules.Actions.Activators;

namespace Workshop.Tests;

[TestClass]
public sealed class ContentReferenceIndexTests
{
    private static readonly Lazy<GameSession> Session = new(() => ContentSession.Build(Path.Combine(AppContext.BaseDirectory, "NoMods"), [], TextWriter.Null));
    private static readonly Lazy<ContentReferenceIndex> Index = new(() => new ContentReferenceIndex(Session.Value.Catalogs));

    private static GameCatalogs Catalogs => Session.Value.Catalogs;

    [TestMethod]
    public void EveryScrollWhoseSpellIsAnAction_UsesItsSpell_AndTheSpellIsUsedByIt()
    {
        var scrolls = Catalogs.ItemCatalog.Definitions
            .Where(item => item.Activator is ScrollActivator scroll && Catalogs.ActionCatalog.TryGet(scroll.SpellId, out _))
            .ToList();
        Assert.IsNotEmpty(scrolls);

        foreach (var scroll in scrolls)
        {
            var spellId = ((ScrollActivator)scroll.Activator!).SpellId;

            var uses = Index.Value.Uses(ContentRecordKind.Item, scroll.Id);
            Assert.IsTrue(uses.Any(reference => reference.Kind == ContentReferenceKind.CastsSpell && reference.To.Kind == ContentRecordKind.Action && reference.To.Id == spellId), $"{scroll.Name} doesn't use its spell.");

            var usedBy = Index.Value.UsedBy(ContentRecordKind.Action, spellId);
            Assert.IsTrue(usedBy.Any(reference => reference.From.Kind == ContentRecordKind.Item && reference.From.Id == scroll.Id), $"{scroll.Name}'s spell isn't used by it.");
        }
    }

    /// <summary>A scroll whose spell id names no action (Scroll of Torch's is a synthesised spell) links to nothing rather than to a record that doesn't exist.</summary>
    [TestMethod]
    public void AScrollWhoseSpellIsNoAction_CastsNothing()
    {
        var scrolls = Catalogs.ItemCatalog.Definitions
            .Where(item => item.Activator is ScrollActivator scroll && !Catalogs.ActionCatalog.TryGet(scroll.SpellId, out _))
            .ToList();
        Assert.IsNotEmpty(scrolls);

        foreach (var scroll in scrolls)
        {
            Assert.IsFalse(Index.Value.Uses(ContentRecordKind.Item, scroll.Id).Any(reference => reference.Kind == ContentReferenceKind.CastsSpell), $"{scroll.Name} casts a spell that isn't an action.");
        }
    }

    [TestMethod]
    public void EveryAuraAnActionOrItemNames_IsGrantedByIt()
    {
        var checkedAny = false;
        foreach (var definition in Catalogs.ActionCatalog.Definitions.Cast<Game.Modules.Actions.ActivatableDefinition>().Concat(Catalogs.ItemCatalog.Definitions))
        {
            var kind = definition is Game.Modules.Actions.ActionDefinition ? ContentRecordKind.Action : ContentRecordKind.Item;
            var namedAuraIds = new HashSet<Guid>();
            EffectEntries.ForEach(definition, entry =>
            {
                foreach (var aura in entry.ReferencedAuras)
                {
                    namedAuraIds.Add(aura.Id);
                }
            });

            foreach (var auraId in namedAuraIds)
            {
                checkedAny = true;
                Assert.IsTrue(Index.Value.Uses(kind, definition.Id).Any(reference => reference.Kind == ContentReferenceKind.GrantsAura && reference.To.Id == auraId), $"{definition.Name} doesn't grant an aura it names.");
                Assert.IsTrue(Index.Value.UsedBy(ContentRecordKind.Aura, auraId).Any(reference => reference.From.Id == definition.Id), $"An aura {definition.Name} names isn't granted by it.");
            }
        }

        Assert.IsTrue(checkedAny, "No built-in action or item names an aura.");
    }

    [TestMethod]
    public void EveryTerrainAura_IsRadiatedByItsTerrain()
    {
        var terrain = Catalogs.Terrain;
        var checkedAny = false;
        for (var typeId = 1; typeId <= terrain.Count; typeId++)
        {
            if (!terrain.TryGet((ushort)typeId, out var definition) || definition.Aura is not { } terrainAura)
            {
                continue;
            }

            checkedAny = true;
            Assert.IsTrue(Index.Value.UsedBy(ContentRecordKind.Aura, terrainAura.Aura.Id).Any(reference =>
                reference.From.Kind == ContentRecordKind.Terrain && reference.From.Name == definition.Name && reference.Kind == ContentReferenceKind.RadiatesAura), $"{definition.Name} doesn't radiate its aura.");
        }

        Assert.IsTrue(checkedAny, "No built-in terrain radiates an aura.");
    }

    [TestMethod]
    public void EveryBlueprintAura_IsRadiatedByItsBlueprint()
    {
        var blueprints = Catalogs.Definitions;
        var checkedAny = false;
        for (var blueprintId = 1; blueprintId <= blueprints.Count; blueprintId++)
        {
            if (!blueprints.TryGet((ushort)blueprintId, out var blueprint))
            {
                continue;
            }

            foreach (var grant in blueprint.Auras)
            {
                checkedAny = true;
                Assert.IsTrue(Index.Value.Uses(ContentRecordKind.Blueprint, blueprint.Id).Any(reference => reference.To.Id == grant.Aura.Id && reference.Kind == ContentReferenceKind.RadiatesAura), $"{blueprint.Name} doesn't radiate its aura.");
            }
        }

        Assert.IsTrue(checkedAny, "No built-in blueprint radiates an aura.");
    }

    [TestMethod]
    public void UsesAndUsedBy_AreTheSameReferencesFromEitherEnd_WithNoDuplicates()
    {
        var everyUse = new List<ContentReference>();
        foreach (var action in Catalogs.ActionCatalog.Definitions)
        {
            everyUse.AddRange(Index.Value.Uses(ContentRecordKind.Action, action.Id));
        }

        foreach (var item in Catalogs.ItemCatalog.Definitions)
        {
            everyUse.AddRange(Index.Value.Uses(ContentRecordKind.Item, item.Id));
        }

        CollectionAssert.AllItemsAreUnique(everyUse);
        foreach (var reference in everyUse)
        {
            CollectionAssert.Contains(Index.Value.UsedBy(reference.To.Kind, reference.To.Id).ToList(), reference);
        }
    }

    [TestMethod]
    public void ARecordNothingNames_HasNoUsedBy()
    {
        Assert.IsEmpty(Index.Value.UsedBy(ContentRecordKind.Action, Guid.NewGuid()));
        Assert.IsEmpty(Index.Value.Uses(ContentRecordKind.Item, Guid.NewGuid()));
    }
}
