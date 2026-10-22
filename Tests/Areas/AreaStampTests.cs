using System.Text;
using Engine.Content;
using Engine.Math;
using Engine.Tags;
using Engine.Tiles;
using Game;
using Game.Areas;
using Game.Blueprints.Composites;
using Game.Blueprints.Objects;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Content;
using Game.Floors;
using Game.Modules.Core.Components;
using Game.Spawning;
using Game.Terrain;
using Game.World;

namespace Tests.Areas;

/// <summary>An area instance is stamped into a neighborhood's plan exactly as its rows say, its residents are the same on every visit, uniques follow the roster, and the starting area reproduces the fixtures it replaced.</summary>
[TestClass]
public sealed class AreaStampTests
{
    private static readonly Guid TestAreaId = new("6b8f3d52-04e9-4a1f-9c37-2d4e0f1a5b01");
    private static readonly Guid TestPlacementId = new("6b8f3d52-04e9-4a1f-9c37-2d4e0f1a5b02");

    /// <summary>The fixtures TestMapBuilder.BuildFixtureEntities spawned before the starting area replaced it, in its order: blueprint, cell from the neighborhood's origin, and size (null: the blueprint's own), each on its blueprint's own layer.</summary>
    private static readonly (Guid Blueprint, int X, int Y, Vector2Byte? Size)[] FixturesBeforeTheStartingArea =
    [
        (LongDescriptionGoblin.Id, 2, 2, new Vector2Byte(2, 2)),
        (GoblinEngineer.Id, 5, 5, new Vector2Byte(3, 3)),
        (StationaryFairyEngineer.Id, 1, 1, null),
        (Fairy.Id, 17, 16, null),
        (GoblinFairy.Id, 17, 9, null),
        (GoblinEngineerTank.Id, 11, 2, null),
        .. Enumerable.Repeat<(Guid, int, int, Vector2Byte?)>((TinyGoblin.Id, 3, 5, null), 4),
        .. Enumerable.Repeat<(Guid, int, int, Vector2Byte?)>((TinyGoblin.Id, 7, 5, null), 11),
        (PhasingFairy.Id, 17, 16, null),
        (GeneralShop.Id, 21, 13, null),
        (PotionShop.Id, 24, 13, null),
        (GoblinForeman.Id, 13, 5, null),
        (HealingShrine.Id, 19, 11, null),
    ];

    private static GameBuildPassResult Build(MapBounds bounds) =>
        BuiltInTestModules.Build(new Map(bounds), new MathUtility(new Random(1)), initialEntityCapacity: 5000, initialComponentCapacity: 5000, crawlerNumbers: new UniqueNumberAllocator(1, 1, 24));

    private static TestMapBuilder BuilderFor(GameBuildPassResult pass)
    {
        var context = pass.Context;
        return new TestMapBuilder(pass.EcsContext.EntityManager, pass.Factory, context.Terrain, context.Auras, context.Definitions, context.Areas, context.UniqueEntities);
    }

    private static AreaDefinition TestArea(IReadOnlyList<string> ground, IReadOnlyList<AreaCell> legend, IReadOnlyList<AreaEntityPlacement>? entities = null, int width = 4, int height = 3) =>
        new(TestAreaId, "Test area", string.Empty, GameplayTagSet.Empty, width, height, new AreaLayers(ground), legend, [], entities ?? [], []);

    [TestMethod]
    public void TheStamp_WritesEveryDefinedCell_AndLeavesTransparentCellsAsGenerated()
    {
        var pass = Build(new MapBounds(0, 0, 40, 30, 3));
        var terrain = pass.Context.Terrain;
        var stoneFloor = terrain.GetId(BuiltInTerrain.StoneFloorKey);
        var stoneWall = terrain.GetId(BuiltInTerrain.StoneWallKey);
        var dirt = terrain.GetId(BuiltInTerrain.DirtKey);
        var lava = terrain.GetId(BuiltInTerrain.LavaKey);
        var layout = pass.World.Map.CreateLayout(0, 0, layoutSeed: 5);
        for (var y = 0; y < 30; y++)
        {
            for (var x = 0; x < 40; x++)
            {
                layout.SetTerrain(x, y, TerrainLayer.Ground, new TerrainCell(dirt));
                layout.SetStructure(new Vector3Int(x, y, (int)MapLayer.Ground), new TerrainCell(stoneWall));
            }
        }

        var area = TestArea(["#. ", "~~ "], [new AreaCell('#', BuiltInTerrain.StoneFloorKey, BuiltInTerrain.StoneWallKey), new AreaCell('.', BuiltInTerrain.StoneFloorKey, ClearsStructure: true), new AreaCell('~', BuiltInTerrain.LavaKey)], width: 3, height: 2);
        var result = new AreaStampResult();
        new AreaStamp(pass.Context.Areas, terrain, pass.Context.Definitions).Apply(layout, new AreaInstance(new AreaInstanceKey(TestPlacementId), area, Seed: 1, OriginX: 10, OriginY: 20), result);
        pass.World.Map.LoadNeighborhood(layout);

        ushort TerrainAt(int x, int y) => pass.World.GetTerrainAt(new Vector3Int(x, y, (int)MapLayer.Ground)).TypeId;
        ushort WallAt(int x, int y) => pass.World.GetStructureAt(new Vector3Int(x, y, (int)MapLayer.Ground)).TypeId;
        Assert.AreEqual(stoneFloor, TerrainAt(10, 20));
        Assert.AreEqual(stoneWall, WallAt(10, 20));
        Assert.AreEqual(stoneFloor, TerrainAt(11, 20));
        Assert.AreEqual(TerrainRegistry.None, WallAt(11, 20));
        Assert.AreEqual(lava, TerrainAt(10, 21));
        Assert.AreEqual(stoneWall, WallAt(10, 21), "A legend entry without a wall keeps the generated one.");
        Assert.AreEqual(dirt, TerrainAt(12, 20), "A space keeps what generation made.");
        Assert.AreEqual(dirt, TerrainAt(9, 20), "Outside the box is untouched.");
        Assert.IsTrue(result.Covers(11, 21, MapLayer.Ground));
        Assert.IsFalse(result.Covers(12, 21, MapLayer.Ground));
        Assert.IsFalse(result.Covers(11, 21, MapLayer.Flying), "A layer the area has no rows for isn't covered.");
    }

    [TestMethod]
    public void ThePlayerSpawn_IsTheStartingAreasMarker()
    {
        var pass = Build(new MapBounds(0, 0, 40, 30, 3));

        Assert.AreEqual(new Vector3Int(17, 13, (int)MapLayer.Ground), FloorBuilder.PlayerSpawnOrigin(pass.Context.Areas));
    }

    [TestMethod]
    public void TheStartingArea_SpawnsEveryFixtureWithTheBlueprintCellSizeAndLayerItHadBefore()
    {
        var pass = Build(new MapBounds(0, 0, 40, 30, 3));
        var definitions = pass.Context.Definitions;

        var plan = BuilderFor(pass).Plan(pass.World.Map, new NeighborhoodRecord(0, 0, seed: 42), populationSeed: 7);

        var expected = FixturesBeforeTheStartingArea
            .Select(fixture => (definitions.GetId(fixture.Blueprint), fixture.X, fixture.Y, fixture.Size, definitions.Resolve(definitions.GetId(fixture.Blueprint)).Layer))
            .ToList();
        var actual = plan.Areas.Spawns
            .Select(spawn => (spawn.Request.BlueprintId, spawn.Request.X, spawn.Request.Y, spawn.Request.Size, spawn.Request.Layer!.Value))
            .ToList();
        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void AnAreasResidents_HaveTheSameSeedsOnEveryVisit_AndDifferFromOneAnother()
    {
        var pass = Build(new MapBounds(0, 0, 40, 30, 3));
        var builder = BuilderFor(pass);
        var record = new NeighborhoodRecord(0, 0, seed: 42);

        var firstVisit = builder.Plan(pass.World.Map, record, populationSeed: 7).Areas.Spawns.Select(spawn => spawn.Request).ToList();
        var secondVisit = builder.Plan(pass.World.Map, record, populationSeed: 8).Areas.Spawns.Select(spawn => spawn.Request).ToList();

        CollectionAssert.AreEqual(firstVisit, secondVisit);
        Assert.AreEqual(firstVisit.Count, firstVisit.Select(request => request.Seed).Distinct().Count());
    }

    [TestMethod]
    public void RandomPopulation_StaysOutOfTheStartingArea_OnEveryLayer()
    {
        var pass = Build(new MapBounds(0, 0, 200, 200, 3));

        var plan = BuilderFor(pass).Plan(pass.World.Map, new NeighborhoodRecord(0, 0, seed: 42), populationSeed: 7);

        var areaSpawns = plan.Areas.Spawns.Select(spawn => spawn.Request).ToHashSet();
        var insideTheBox = plan.Spawns.Where(spawn => !areaSpawns.Contains(spawn) && spawn.X < 32 && spawn.Y < 32).ToList();
        Assert.IsEmpty(insideTheBox);
        Assert.IsTrue(plan.Spawns.Any(spawn => spawn.X >= 32 || spawn.Y >= 32), "Precondition: the rest of the neighborhood is populated.");
    }

    [TestMethod]
    public void AUniquePlacement_SpawnsOnce_ThenItsFallback_AndAgainOnlyOnceTheFirstIsGoneWithoutDying()
    {
        var pass = Build(new MapBounds(0, 0, 80, 40, 3));
        var context = pass.Context;
        var area = TestArea(["    ", "    ", "    "], [], [new AreaEntityPlacement("foreman", 0, 0, GoblinForeman.Id, Unique: true, Fallback: Goblin.Id)]);
        context.Areas.Register(area);
        context.Areas.Register(new AreaPlacement(TestPlacementId, TestAreaId, 0, 0, 40, 0));
        var builder = BuilderFor(pass);
        var record = new NeighborhoodRecord(0, 0, seed: 42);
        var foreman = context.Definitions.GetId(GoblinForeman.Id);
        var goblin = context.Definitions.GetId(Goblin.Id);

        int SpawnedAtTheUniquesCell()
        {
            var plan = builder.Plan(pass.World.Map, record, populationSeed: 7);
            var uniqueIndex = plan.UniqueSpawns.Single().Key;
            foreach (var _ in builder.Spawn(new NeighborhoodPlan(plan.Layout, plan.AuraCellsByRow, plan.Spawns.Take(uniqueIndex + 1).ToList(), [uniqueIndex + 1], plan.Areas, plan.UniqueSpawns)))
            {
            }

            return pass.World.GetEntityIdAt(new Vector3Int(40, 0, (int)MapLayer.Ground));
        }

        var spawnRecords = pass.EcsContext.ComponentManager.GetDirectPool<SpawnRecordComponent>();
        var first = SpawnedAtTheUniquesCell();
        Assert.AreEqual(foreman, spawnRecords.GetReadonly(first).BlueprintId);

        pass.World.RemoveEntityFromMap(first, ref pass.EcsContext.ComponentManager.GetDirectPool<TransformComponent>().Get(first));
        var second = SpawnedAtTheUniquesCell();
        Assert.AreEqual(goblin, spawnRecords.GetReadonly(second).BlueprintId, "One lives already, so the fallback spawns.");

        pass.EcsContext.EntityManager.DestroyEntity(first);
        pass.EcsContext.EntityManager.DestroyEntity(second);
        var third = SpawnedAtTheUniquesCell();
        Assert.AreEqual(foreman, spawnRecords.GetReadonly(third).BlueprintId, "Destroyed without dying, it may come back.");
        Assert.AreEqual(AreaSeeds.ForUnique(TestAreaId, "foreman"), spawnRecords.GetReadonly(third).Seed, "A unique is the same individual wherever it appears.");
    }

    [TestMethod]
    public void AKilledUnique_NeverComesBack()
    {
        var roster = new UniqueEntityRoster();
        roster.Placed(GoblinForeman.Id, entityId: 3);

        roster.EntityDied(3);
        roster.EntityDestroying(3);

        Assert.IsFalse(roster.IsAvailable(GoblinForeman.Id));
        Assert.IsTrue(roster.WasKilled(GoblinForeman.Id));
    }

    [TestMethod]
    public void ChanceAndOneOf_RollFromTheInstanceSeed()
    {
        var pass = Build(new MapBounds(0, 0, 40, 30, 3));
        var context = pass.Context;
        var row = new string('g', 20);
        var area = TestArea([row, row, row], [new AreaCell('g', Spawns: [new AreaSpawn(OneOf: [new AreaWeightedBlueprint(TinyGoblin.Id), new AreaWeightedBlueprint(Fairy.Id)], Chance: 50)])], width: 20);
        var stamp = new AreaStamp(context.Areas, context.Terrain, context.Definitions);

        IReadOnlyList<SpawnRequest> SpawnsWithSeed(ulong seed)
        {
            var result = new AreaStampResult();
            stamp.Apply(pass.World.Map.CreateLayout(0, 0, layoutSeed: 5), new AreaInstance(new AreaInstanceKey(TestPlacementId), area, seed, 0, 0), result);
            return [.. result.Spawns.Select(spawn => spawn.Request)];
        }

        var first = SpawnsWithSeed(1);
        CollectionAssert.AreEqual(first.ToList(), SpawnsWithSeed(1).ToList());
        CollectionAssert.AreNotEqual(first.ToList(), SpawnsWithSeed(2).ToList());
        Assert.IsTrue(first.Count is > 10 and < 50, $"About half of 60 cells spawn; {first.Count} did.");
        Assert.AreEqual(2, first.Select(request => request.BlueprintId).Distinct().Count());
    }

    [TestMethod]
    public void TheShippedAreaFiles_AreCanonical()
    {
        var types = Build(new MapBounds(0, 0, 40, 30, 3)).Context.ContentTypes;
        var paths = Directory.GetFiles(Path.Combine(MasterContent.DefaultDirectory, MasterContent.AreasDirectoryName), MasterContent.AreaFilePattern);
        Assert.IsNotEmpty(paths);

        foreach (var path in paths)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var failures = new List<ContentFailure>();
            var record = ContentDocument.Read<AreaDefinition>(text, path, MasterContent.AreasTable, types, NoContentReferences.Instance, failures);

            Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
            Assert.AreEqual(text, ContentDocument.Write(MasterContent.AreasTable, record!.Value!, types), $"{Path.GetFileName(path)} isn't written as the writer writes it.");
        }
    }
}
