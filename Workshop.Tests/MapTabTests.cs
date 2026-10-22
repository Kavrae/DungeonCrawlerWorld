using Builders.MapAreas;
using Builders.References;
using DungeonCrawlerWorld.Workshop;
using Engine.Math;
using Engine.Tiles;
using Game.Areas;
using Game.Blueprints.Composites;
using Game.Bootstrap;
using Game.Modules.Core.Components;
using Game.Terrain;
using Game.World;

namespace Workshop.Tests;

/// <summary>The Map tab shows each area as the game builds it, groups its records, and links areas, palettes, placements, terrain and blueprints both ways.</summary>
[TestClass]
public sealed class MapTabTests
{
    private static readonly Lazy<ValidatedMods> Mods = new(() => ContentSession.ValidateMods(Path.Combine(AppContext.BaseDirectory, "NoMods"), []));
    private static readonly Lazy<GameSession> Session = new(() => ContentSession.Build(Mods.Value, [], TextWriter.Null));
    private static readonly Lazy<ContentReferenceIndex> Index = new(() => new ContentReferenceIndex(Session.Value.Catalogs));

    private static GameCatalogs Catalogs => Session.Value.Catalogs;

    private static AreaDefinition StartingArea => Catalogs.Areas.Areas.Definitions.Single(area => area.Name == "Starting area");

    [TestMethod]
    public void ThePreviewBuildOfEveryShippedArea_MatchesAStampIntoAPlainLayout_CellForCell()
    {
        var areas = Catalogs.Areas.Areas.Definitions.ToList();
        Assert.IsNotEmpty(areas);

        foreach (var area in areas)
        {
            var preview = AreaPreviewBuild.Build(area, seed: 3, map => ContentSession.BuildPreview(Mods.Value, map, []));

            var expectedMap = new Map(new MapBounds(0, 0, area.Width, area.Height, 3));
            var layout = expectedMap.CreateLayout(0, 0, layoutSeed: 0);
            AreaPreviewBuild.Fill(layout, Catalogs.Terrain);
            var expectedStamp = new AreaStampResult();
            new AreaStamp(Catalogs.Areas, Catalogs.Terrain, Catalogs.Definitions).Apply(layout, new AreaInstance(new AreaInstanceKey(area.Id), area, 3, 0, 0), expectedStamp);
            expectedMap.LoadNeighborhood(layout);

            var world = preview.Session.World;
            var previewTerrain = preview.Session.Catalogs.Terrain;
            for (var y = 0; y < area.Height; y++)
            {
                for (var x = 0; x < area.Width; x++)
                {
                    foreach (var terrainLayer in Enum.GetValues<TerrainLayer>())
                    {
                        Assert.AreEqual(KeyOf(Catalogs.Terrain, expectedMap.GetTerrain(x, y, terrainLayer).TypeId), KeyOf(previewTerrain, world.Map.GetTerrain(x, y, terrainLayer).TypeId), $"{area.Name} floor at ({x}, {y}) on {terrainLayer}.");
                    }

                    foreach (var layer in Enum.GetValues<MapLayer>())
                    {
                        var position = new Vector3Int(x, y, (int)layer);
                        Assert.AreEqual(KeyOf(Catalogs.Terrain, expectedMap.GetStructure(position).TypeId), KeyOf(previewTerrain, world.GetStructureAt(position).TypeId), $"{area.Name} wall at ({x}, {y}) on {layer}.");
                    }
                }
            }

            Assert.HasCount(expectedStamp.Spawns.Count, preview.Stamp.Spawns);
            foreach (var spawn in preview.Stamp.Spawns)
            {
                var position = new Vector3Int(spawn.Request.X, spawn.Request.Y, (int)spawn.Request.Layer!.Value);
                Assert.IsTrue(world.GetEntityIdAt(position) >= 0 || world.GetOccupantEntityIdsAt(position).Count > 0, $"{area.Name}: nothing spawned at {position} for {spawn.PlacementId}.");
            }
        }
    }

    [TestMethod]
    public void TheBrowser_ListsPlacedAreasPalettesAndPlacementsUnderTheirOwnHeadings()
    {
        var records = MapContentRecord.AllIn(Catalogs.Areas);

        var sections = MapBrowserSections.Build(records, Catalogs.Areas, string.Empty, Catalogs.GameplayTags);

        var headings = sections.Select(section => section.Heading).ToList();
        CollectionAssert.AreEqual(new[] { MapBrowserSections.PlacedHeading, MapBrowserSections.PalettesHeading, MapBrowserSections.PlacementsHeading }, headings);
        Assert.IsTrue(sections[0].Records.Any(record => record.Area == StartingArea));
        Assert.IsTrue(sections[2].Records.All(record => record.Name.StartsWith("Starting area at (0, 0)", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TheBrowserSearch_FindsAnAreaByItsTag()
    {
        var sections = MapBrowserSections.Build(MapContentRecord.AllIn(Catalogs.Areas), Catalogs.Areas, "Testing", Catalogs.GameplayTags);

        Assert.IsTrue(sections.SelectMany(section => section.Records).Any(record => record.Area == StartingArea));
    }

    [TestMethod]
    public void AnArea_UsesItsPaletteTerrainAndBlueprints_AndIsUsedByItsPlacement()
    {
        var uses = Index.Value.Uses(ContentRecordKind.Area, StartingArea.Id);

        Assert.IsTrue(uses.Any(reference => reference.Kind == ContentReferenceKind.UsesPalette && reference.To.Kind == ContentRecordKind.AreaPalette));
        Assert.IsTrue(uses.Any(reference => reference.Kind == ContentReferenceKind.UsesTerrain && reference.To.Name == HolyGroundName));
        Assert.IsTrue(uses.Any(reference => reference.Kind == ContentReferenceKind.PlacesBlueprint && reference.To.Id == GoblinForeman.Id));

        var usedBy = Index.Value.UsedBy(ContentRecordKind.Area, StartingArea.Id);
        Assert.IsTrue(usedBy.Any(reference => reference.Kind == ContentReferenceKind.PlacesArea && reference.From.Kind == ContentRecordKind.AreaPlacement));
    }

    [TestMethod]
    public void APalette_IsUsedByTheAreasThatNameIt_AndLaysItsWalls()
    {
        var palette = Catalogs.Areas.Palettes.Definitions.Single();

        Assert.IsTrue(Index.Value.UsedBy(ContentRecordKind.AreaPalette, palette.Id).Any(reference => reference.From.Id == StartingArea.Id));
        Assert.IsTrue(Index.Value.Uses(ContentRecordKind.AreaPalette, palette.Id).Any(reference => reference.Kind == ContentReferenceKind.UsesTerrain));
        Assert.AreEqual(MapTab.TabTitle, ContentLinks.TabTitleFor(ContentRecordKind.AreaPalette));
    }

    [TestMethod]
    public void TheHoverReadout_NamesTheCellsSymbolFloorAndMarker()
    {
        var preview = AreaPreviewBuild.Build(StartingArea, seed: 0, map => ContentSession.BuildPreview(Mods.Value, map, []));

        var lines = AreaCellReadout.Describe(preview, 17, 13, MapLayer.Ground);

        Assert.HasCount(AreaCellReadout.LineCount, lines);
        StringAssert.Contains(lines[0], "(17, 13) on Ground: symbol ','");
        StringAssert.Contains(lines[4], "player-spawn");
        StringAssert.Contains(AreaCellReadout.Describe(preview, 14, 13, MapLayer.Ground)[1], HolyGroundName);
        StringAssert.Contains(AreaCellReadout.Describe(preview, 13, 5, MapLayer.Ground)[3], "goblin-foreman");
    }

    private static string HolyGroundName => Catalogs.Terrain.TryGet(Catalogs.Terrain.GetId(BuiltInTerrain.HolyGroundKey), out var holyGround) ? holyGround.Name : string.Empty;

    [TestMethod]
    public void TheNeighborhoodPreview_IsTheGamesOwnPlanForTheRecordTheSeedGives_WithTheStartingAreaStamped()
    {
        const int worldSeed = 7;
        var preview = NeighborhoodPreviewBuild.Build(worldSeed, 0, 0, map => ContentSession.BuildPreview(Mods.Value, map, []));

        var (record, populationSeed) = Game.Floors.FloorBuilder.StartingNeighborhoodRecords(worldSeed).Single(entry => entry.Record.CellX == 0 && entry.Record.CellY == 0);
        Assert.AreEqual(record.Seed, preview.Record.Seed);
        Assert.AreEqual(populationSeed, preview.PopulationSeed);
        Assert.AreEqual(StartingArea.Id, preview.Stamp.Instances.Single().Definition.Id);

        var world = preview.Session.World;
        var holyGround = preview.Session.Catalogs.Terrain.GetId(BuiltInTerrain.HolyGroundKey);
        Assert.AreEqual(holyGround, world.GetTerrainAt(new Vector3Int(14, 13, (int)MapLayer.Ground)).TypeId);
        Assert.IsGreaterThan(1000, preview.Session.EcsContext.EntityManager.LivingEntityCount, "The rest of the neighborhood is populated.");
        StringAssert.Contains(AreaCellReadout.Describe(preview, 14, 13, MapLayer.Ground)[0], "Starting area (14, 13)");
        StringAssert.Contains(AreaCellReadout.Describe(preview, 500, 500, MapLayer.Ground)[0], "generated");
    }

    [TestMethod]
    public void ANeighborhoodBuild_RunsOnAWorker_OnceForEachKey_AndIsKeptWhenHandedBack()
    {
        var cache = new AreaPreviewCache(map => ContentSession.BuildPreview(Mods.Value, map, []));
        var key = new NeighborhoodPreviewKey(7, 1, 0);

        var first = cache.BuildNeighborhoodAsync(key);
        var again = cache.BuildNeighborhoodAsync(key);

        Assert.AreSame(first, again);
        Assert.IsFalse(cache.TryGetNeighborhood(key, out _));
        var build = first.GetAwaiter().GetResult();
        cache.KeepNeighborhood(key, build);
        Assert.IsTrue(cache.TryGetNeighborhood(key, out var kept));
        Assert.AreSame(build, kept);
        Assert.IsEmpty(build.Stamp.Instances, "Neighborhood (1, 0) has no fixed placement.");
    }

    private static string KeyOf(TerrainRegistry terrain, ushort typeId) => terrain.TryGet(typeId, out var definition) ? definition.Key : "none";
}
