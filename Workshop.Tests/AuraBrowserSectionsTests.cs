using Builders.Auras;
using Builders.References;
using DungeonCrawlerWorld.Workshop;
using Game.Bootstrap;
using Game.Modules.Auras;

namespace Workshop.Tests;

[TestClass]
public sealed class AuraBrowserSectionsTests
{
    private static readonly Lazy<GameSession> Session = new(() => ContentSession.Build(Path.Combine(AppContext.BaseDirectory, "NoMods"), [], TextWriter.Null));
    private static readonly Lazy<ContentReferenceIndex> Index = new(() => new ContentReferenceIndex(Session.Value.Catalogs));

    private static List<AuraDefinition> AllAuras()
    {
        var auras = Session.Value.Catalogs.Auras;
        return [.. Enumerable.Range(0, auras.Count).Select(auraId => auras.Get((byte)auraId))];
    }

    private static IReadOnlyList<Builders.Browsing.RecordBrowserSection<AuraDefinition>> Build(string search) =>
        AuraBrowserSections.Build(AllAuras(), Index.Value, search, Session.Value.Catalogs.GameplayTags);

    [TestMethod]
    public void EmptySearch_ListsEveryAuraOnce()
    {
        var listed = Build(string.Empty).SelectMany(section => section.Records).ToList();

        Assert.HasCount(Session.Value.Catalogs.Auras.Count, listed);
        CollectionAssert.AllItemsAreUnique(listed);
    }

    [TestMethod]
    public void TerrainAuras_AreUnderTerrain()
    {
        var terrain = Session.Value.Catalogs.Terrain;
        var terrainSection = Build(string.Empty).Single(section => section.Heading == AuraBrowserSections.TerrainHeading);

        for (var typeId = 1; typeId <= terrain.Count; typeId++)
        {
            if (terrain.TryGet((ushort)typeId, out var definition) && definition.Aura is { } terrainAura)
            {
                Assert.IsTrue(terrainSection.Records.Any(aura => aura.Id == terrainAura.Aura.Id), $"{definition.Name}'s aura isn't under Terrain.");
            }
        }
    }

    [TestMethod]
    public void Sections_KeepTheirFixedOrder()
    {
        var headingsInOrder = AuraBrowserSections.HeadingsInOrder.ToList();
        var headingIndexes = Build(string.Empty).Select(section => headingsInOrder.IndexOf(section.Heading)).ToList();

        CollectionAssert.DoesNotContain(headingIndexes, -1);
        CollectionAssert.AreEqual(headingIndexes.Order().ToList(), headingIndexes);
    }

    [TestMethod]
    public void EveryListedAura_ExceptUnusedOnes_IsUsedBySomething()
    {
        foreach (var section in Build(string.Empty).Where(section => section.Heading != AuraBrowserSections.UnusedHeading))
        {
            foreach (var aura in section.Records)
            {
                Assert.IsNotEmpty(Index.Value.UsedBy(ContentRecordKind.Aura, aura.Id), $"{aura.Name} is under {section.Heading} but nothing uses it.");
            }
        }
    }
}
