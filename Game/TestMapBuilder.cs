using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Utilities;
using Game.Blueprints.Composites;
using Game.Blueprints.Objects;
using Game.Blueprints.Races;
using Game.Floors;
using Game.Spawning;
using Game.Modules.Core.Components;
using Game.Modules.Crawler.Components;
using Game.Terrain;
using Game.World;
using Game.Blueprints;

namespace Game;

/// <summary>
/// Builds test neighborhoods across all three MapLayers, each layer with its own independent,
/// percentage-rolled population: Ground (randomized lava/dirt/grass terrain, a Goblin/Fairy/Ghost on
/// GroundPopulationPercent of free tiles per PopulateGroundEntity's breakdown), UnderGround (a
/// randomized dirt/lava mixture, a Ghost on UnderGroundGhostPercent of tiles per
/// PopulateUnderGroundGhost), and Flying (a Fairy on FlyingFairyPercent of tiles per
/// PopulateFlyingFairy) -- plus, in the starting neighborhood only, a hallway cross of walls, a
/// handful of standalone multi-trait fixtures and the shops, via the Blueprint composition system.
/// </summary>
/// <remarks>
/// A stand-in for real template generation. Each neighborhood is
/// generated from its own NeighborhoodRecord, never from shared random state, and the world has no
/// border walls: it has no edge.
/// </remarks>
public sealed class TestMapBuilder(EntityManager entityManager, EntityFactory factory, TerrainRegistry terrain, BlueprintRegistry definitions)
{
    // TEMPORARY: halved once already from the original values (10/5/5) to reduce the creature
    // population -- Movement/HealthRegen/ContactDamage/StatusEffectAura all iterate this
    // population every frame, and at the original density the game was effectively
    // unplayable (5-10fps) for manual testing. Revert that half once the performance
    // investigation these values are standing in for (see TODO.md) lands a real fix. Halved
    // again on top of that for Combat Overhaul: Dodge (TODO.md) -- the new deliberate, telegraphed
    // combat style (windups to react to, a Dodge with a real cooldown cost) wants noticeably fewer
    // simultaneous attackers than the old spam-actions style did; this second halving is a
    // playtesting-tunable starting point, not a measured target.
    private const int GroundPopulationPercent = 3;
    private const int UnderGroundGhostPercent = 2;
    private const int FlyingFairyPercent = 2;

    /// <summary>Chance any given rolled NPC (see BuildRaceEntity) is also a Crawler -- deliberately small; most NPCs are not.</summary>
    private const int CrawlerPercent = 2;

    /// <summary>The terrain registry, with the sprite variants of every terrain this builder writes already resolved -- on the main thread, at construction -- so Plan's TerrainRegistry.CreateCell only reads the registry's cache from a worker.</summary>
    private readonly TerrainRegistry _terrain = WithVariantsResolved(terrain, BuiltInTerrain.StoneFloorKey, BuiltInTerrain.StoneWallKey, BuiltInTerrain.DirtKey, BuiltInTerrain.LavaKey, BuiltInTerrain.GrassKey);

    private readonly ushort _stoneFloor = terrain.GetId(BuiltInTerrain.StoneFloorKey);
    private readonly ushort _stoneWall = terrain.GetId(BuiltInTerrain.StoneWallKey);
    private readonly ushort _dirt = terrain.GetId(BuiltInTerrain.DirtKey);
    private readonly ushort _lava = terrain.GetId(BuiltInTerrain.LavaKey);
    private readonly ushort _grass = terrain.GetId(BuiltInTerrain.GrassKey);
    private readonly ushort _goblin = definitions.GetId(Goblin.Id);
    private readonly ushort _fairy = definitions.GetId(Fairy.Id);
    private readonly ushort _ghost = definitions.GetId(Ghost.Id);

    /// <summary>The starting neighborhood's fixtures, each as the parts it is made of -- see BuildFixtureEntities.</summary>
    private readonly ushort _longDescriptionGoblin = definitions.GetId(LongDescriptionGoblin.Id);

    /// <inheritdoc cref="_longDescriptionGoblin"/>
    private readonly ushort _goblinEngineer = definitions.GetId(GoblinEngineer.Id);

    /// <inheritdoc cref="_longDescriptionGoblin"/>
    private readonly ushort _goblinForeman = definitions.GetId(GoblinForeman.Id);

    /// <inheritdoc cref="_longDescriptionGoblin"/>
    private readonly ushort _stationaryFairyEngineer = definitions.GetId(StationaryFairyEngineer.Id);

    /// <inheritdoc cref="_longDescriptionGoblin"/>
    private readonly ushort _multiRace = definitions.GetId(GoblinFairy.Id);

    /// <inheritdoc cref="_longDescriptionGoblin"/>
    private readonly ushort _multiClass = definitions.GetId(GoblinEngineerTank.Id);

    /// <inheritdoc cref="_longDescriptionGoblin"/>
    private readonly ushort _tinyGoblin = definitions.GetId(TinyGoblin.Id);

    /// <inheritdoc cref="_longDescriptionGoblin"/>
    private readonly ushort _phasingFairy = definitions.GetId(PhasingFairy.Id);

    /// <inheritdoc cref="_longDescriptionGoblin"/>
    private readonly ushort _generalShop = definitions.GetId(GeneralShop.Id);

    /// <inheritdoc cref="_longDescriptionGoblin"/>
    private readonly ushort _potionShop = definitions.GetId(PotionShop.Id);

    /// <summary>The neighborhood holding the hallway cross, the fixtures, the shops and the player's spawn.</summary>
    public const int StartingCellX = 0;

    /// <inheritdoc cref="StartingCellX"/>
    public const int StartingCellY = 0;

    /// <summary>Where FloorBuilder aims the player: inside the hallway cross's corridor gap, counted from the starting neighborhood's origin, so nothing stands between the player and the neighborhood border 17 tiles west.</summary>
    public const int SpawnColumn = 17;

    /// <summary>The spawn row, inside the corridor gap (rows 10-16 are open at the wall columns).</summary>
    public const int SpawnRow = 13;

    /// <summary>
    /// Generates every loaded neighborhood, in row-major order. World is built by the
    /// caller (not here) because it must exist before MovementModule -- itself a constructor
    /// dependency of Bootstrapper.Build, which is what produces the EntityManager/ComponentManager
    /// this builder needs -- so World can't wait until after that call to be created.
    /// </summary>
    /// <remarks>
    /// Every record is assigned, and every population seed drawn, before any neighborhood is generated,
    /// so the seeds a session's first window gets never depend on how much generation consumed. The
    /// plans are made in parallel and applied in order, which gives the same world as making them one by
    /// one.
    /// </remarks>
    public void Populate(World.World world, NeighborhoodRecords records)
    {
        var mapBounds = world.Map.Bounds;
        var neighborhoodsToGenerate = new List<(NeighborhoodRecord Record, int PopulationSeed)>();
        for (var cellY = Neighborhoods.CellOf(mapBounds.MinY); cellY <= Neighborhoods.CellOf(mapBounds.MaxY - 1); cellY++)
        {
            for (var cellX = Neighborhoods.CellOf(mapBounds.MinX); cellX <= Neighborhoods.CellOf(mapBounds.MaxX - 1); cellX++)
            {
                if (world.Map.IsNeighborhoodLoaded(cellX, cellY))
                {
                    var neighborhoodRecord = records.GetOrCreate(cellX, cellY);
                    neighborhoodsToGenerate.Add((neighborhoodRecord, neighborhoodRecord.NextPopulationSeed()));
                }
            }
        }

        var neighborhoodPlans = new NeighborhoodPlan[neighborhoodsToGenerate.Count];
        Parallel.For(0, neighborhoodsToGenerate.Count, neighborhoodIndex => neighborhoodPlans[neighborhoodIndex] = Plan(world.Map, neighborhoodsToGenerate[neighborhoodIndex].Record, neighborhoodsToGenerate[neighborhoodIndex].PopulationSeed));

        foreach (var neighborhoodPlan in neighborhoodPlans)
        {
            world.Map.LoadNeighborhood(neighborhoodPlan.Layout);
            foreach (var _ in Spawn(neighborhoodPlan))
            {
            }
        }
    }

    /// <summary>Generates one neighborhood on this thread -- its layout, loaded whole, then its population a row at a time -- drawing the record's next population seed.</summary>
    /// <remarks>Yields 0 once the layout is loaded, then what Spawn yields.</remarks>
    public IEnumerable<int> GenerateNeighborhood(World.World world, NeighborhoodRecord record)
    {
        var neighborhoodPlan = Plan(world.Map, record, record.NextPopulationSeed());
        world.Map.LoadNeighborhood(neighborhoodPlan.Layout);
        yield return 0;

        foreach (var createdEntityCount in Spawn(neighborhoodPlan))
        {
            yield return createdEntityCount;
        }
    }

    /// <summary>Decides one neighborhood -- its terrain and walls from record.Seed, its creatures from populationSeed -- without touching the world: safe on a worker thread.</summary>
    /// <remarks>
    /// Reads only the record's seed, the map's fixed shape (Map.CreateLayout) and what this builder
    /// resolved at construction, so the plan doesn't depend on what else was generated first, or on
    /// which thread made it. The layout is decided whole before the population, so a creature is
    /// always placed against the walls it will actually stand beside.
    /// </remarks>
    /// <param name="planningCancellation">Checked once a row: a plan no longer wanted stops early and throws OperationCanceledException.</param>
    public NeighborhoodPlan Plan(Map map, NeighborhoodRecord record, int populationSeed, CancellationToken planningCancellation = default)
    {
        var neighborhoodLayout = map.CreateLayout(record.CellX, record.CellY);
        var layoutRolls = new MathUtility(new Random(record.Seed));
        for (var row = neighborhoodLayout.MinY; row < neighborhoodLayout.MaxY; row++)
        {
            planningCancellation.ThrowIfCancellationRequested();
            for (var column = neighborhoodLayout.MinX; column < neighborhoodLayout.MaxX; column++)
            {
                GenerateLayoutCell(neighborhoodLayout, layoutRolls, column, row);
            }
        }

        var population = new Population(new MathUtility(new Random(populationSeed)));
        var spawnRowEnds = new List<int>(neighborhoodLayout.MaxY - neighborhoodLayout.MinY + 1);
        for (var row = neighborhoodLayout.MinY; row < neighborhoodLayout.MaxY; row++)
        {
            planningCancellation.ThrowIfCancellationRequested();
            for (var column = neighborhoodLayout.MinX; column < neighborhoodLayout.MaxX; column++)
            {
                PopulateCell(population, column, row);
            }

            spawnRowEnds.Add(population.Spawns.Count);
        }

        if (record.CellX == StartingCellX && record.CellY == StartingCellY)
        {
            BuildFixtureEntities(population);
            spawnRowEnds.Add(population.Spawns.Count);
        }

        return new NeighborhoodPlan(neighborhoodLayout, TerrainAuraSources.ByRow(neighborhoodLayout, _terrain), population.Spawns, spawnRowEnds);
    }

    /// <summary>Spawns neighborhoodPlan's creatures in order, a row at a time, once its layout is loaded. Main thread only.</summary>
    /// <remarks>Yields after each row with the number of entities it created, so a caller can spread it over frames. A creature that can't be placed is destroyed rather than left off the map.</remarks>
    public IEnumerable<int> Spawn(NeighborhoodPlan neighborhoodPlan)
    {
        var nextSpawnIndex = 0;
        foreach (var spawnRowEnd in neighborhoodPlan.SpawnRowEnds)
        {
            var livingBefore = entityManager.LivingEntityCount;
            for (; nextSpawnIndex < spawnRowEnd; nextSpawnIndex++)
            {
                factory.Spawn(neighborhoodPlan.Spawns[nextSpawnIndex]);
            }

            yield return entityManager.LivingEntityCount - livingBefore;
        }
    }

    /// <summary>The random sequence one neighborhood's population is rolled from, and the spawns it has rolled so far.</summary>
    private sealed class Population(MathUtility rolls)
    {
        public MathUtility Rolls { get; } = rolls;

        public List<SpawnRequest> Spawns { get; } = [];
    }

    private static TerrainRegistry WithVariantsResolved(TerrainRegistry terrainRegistry, params string[] terrainKeys)
    {
        foreach (var terrainKey in terrainKeys)
        {
            terrainRegistry.GetVariantCount(terrainRegistry.GetId(terrainKey));
        }

        return terrainRegistry;
    }

    /// <summary>
    /// Ground: stone floor under a wall where the starting neighborhood's hallway cross runs, randomized
    /// terrain everywhere else (see PickGroundTerrain). UnderGround: a randomized dirt/lava mixture --
    /// its own independent roll from Ground's mix, so the two layers don't mirror each other.
    /// </summary>
    private void GenerateLayoutCell(NeighborhoodLayout neighborhoodLayout, MathUtility layoutRolls, int column, int row)
    {
        if (IsHallwayWall(column, row))
        {
            BuildTerrain(neighborhoodLayout, layoutRolls, _stoneFloor, column, row, TerrainLayer.Ground);
            BuildWall(neighborhoodLayout, layoutRolls, column, row, MapLayer.Ground);
        }
        else
        {
            BuildTerrain(neighborhoodLayout, layoutRolls, PickGroundTerrain(layoutRolls), column, row, TerrainLayer.Ground);
        }

        BuildTerrain(neighborhoodLayout, layoutRolls, layoutRolls.Next(0, 20) == 0 ? _lava : _dirt, column, row, TerrainLayer.UnderGround);
    }

    private void PopulateCell(Population population, int column, int row)
    {
        if (!IsHallwayWall(column, row) && population.Rolls.Next(0, 100) < GroundPopulationPercent)
        {
            PopulateGroundEntity(population, column, row);
        }

        if (population.Rolls.Next(0, 100) < UnderGroundGhostPercent)
        {
            PopulateUnderGroundGhost(population, column, row);
        }

        // Flying layer: no walls of its own, so every cell is eligible.
        if (population.Rolls.Next(0, 100) < FlyingFairyPercent)
        {
            PopulateFlyingFairy(population, column, row);
        }
    }

    /// <summary>The starting neighborhood's hallway cross: wall columns 10 and 16 and wall rows 10 and 16, open where they meet.</summary>
    private static bool IsHallwayWall(int column, int row)
    {
        if (Neighborhoods.CellOf(column) != StartingCellX || Neighborhoods.CellOf(row) != StartingCellY)
        {
            return false;
        }

        var x = column - Neighborhoods.OriginOf(StartingCellX);
        var y = row - Neighborhoods.OriginOf(StartingCellY);
        return (x is 10 or 16 && (y < 10 || y > 16)) ||
            (y is 10 or 16 && (x < 10 || x > 16));
    }

    /// <summary>
    /// Ground layer's entity roll (see GroundPopulationPercent for the gate already applied
    /// by the caller): 40% 1x1 Goblin, 8% 2x2 Goblin, 1% 3x3 Goblin, 40% 1x1 Fairy, 8% 2x2
    /// Fairy, 1% 3x3 Fairy, 2% 1x2 Ghost -- a 0-99 roll so each share lands exactly. All three
    /// races land on the Ground layer here, including Fairy/Ghost -- distinct from, and in
    /// addition to, the dedicated Ghost-on-UnderGround and Fairy-on-Flying populations below.
    /// </summary>
    private void PopulateGroundEntity(Population population, int column, int row)
    {
        var roll = population.Rolls.Next(0, 100);
        switch (roll)
        {
            case < 40:
                BuildRaceEntity(population, _goblin, column, row, new Vector2Byte(1, 1), MapLayer.Ground);
                break;
            case < 48:
                BuildRaceEntity(population, _goblin, column, row, new Vector2Byte(2, 2), MapLayer.Ground);
                break;
            case < 49:
                BuildRaceEntity(population, _goblin, column, row, new Vector2Byte(3, 3), MapLayer.Ground);
                break;
            case < 89:
                BuildRaceEntity(population, _fairy, column, row, new Vector2Byte(1, 1), MapLayer.Ground);
                break;
            case < 97:
                BuildRaceEntity(population, _fairy, column, row, new Vector2Byte(2, 2), MapLayer.Ground);
                break;
            case < 98:
                BuildRaceEntity(population, _fairy, column, row, new Vector2Byte(3, 3), MapLayer.Ground);
                break;
            default:
                BuildRaceEntity(population, _ghost, column, row, new Vector2Byte(1, 2), MapLayer.Ground);
                break;
        }
    }

    /// <summary>UnderGround layer's dedicated Ghost population (see UnderGroundGhostPercent for the gate): 90% 1x1, 9% 2x2, 1% 3x3.</summary>
    private void PopulateUnderGroundGhost(Population population, int column, int row)
    {
        var size = population.Rolls.Next(0, 100) switch
        {
            < 90 => new Vector2Byte(1, 1),
            < 99 => new Vector2Byte(2, 2),
            _ => new Vector2Byte(3, 3),
        };

        BuildRaceEntity(population, _ghost, column, row, size, MapLayer.UnderGround);
    }

    /// <summary>Flying layer's dedicated Fairy population (see FlyingFairyPercent for the gate): 90% 1x1, 9% 2x2, 1% 3x3.</summary>
    private void PopulateFlyingFairy(Population population, int column, int row)
    {
        var size = population.Rolls.Next(0, 100) switch
        {
            < 90 => new Vector2Byte(1, 1),
            < 99 => new Vector2Byte(2, 2),
            _ => new Vector2Byte(3, 3),
        };

        BuildRaceEntity(population, _fairy, column, row, size, MapLayer.Flying);
    }

    /// <summary>Rolls one creature at the given size/layer -- the shared path for every PopulateEntity roll outcome. A small percentage also become Crawlers (see CrawlerPercent).</summary>
    /// <remarks>Everything the spawn itself involves -- the tier-first entity id, the build or the skeleton, the placement, the spawn move and the crawler number -- is EntityFactory's, when Spawn applies the plan; what is decided here is what this map's own population rules decide: the blueprint, the layer, the footprint, the seed and the crawler roll. A creature that won't fit where it was rolled still used its rolls, so it doesn't shift every later roll in this neighborhood.</remarks>
    private void BuildRaceEntity(Population population, ushort blueprintId, int column, int row, Vector2Byte size, MapLayer mapLayer)
    {
        var seed = population.Rolls.NextSeed();
        var isCrawler = population.Rolls.Next(0, 100) < CrawlerPercent;

        population.Spawns.Add(new SpawnRequest(blueprintId, column, row) { Layer = mapLayer, Size = size, Seed = seed, Crawler = isCrawler });
    }

    /// <summary>
    /// Terrain (the floor an entity stands on) is a cell, not an entity -- see TerrainDefinition.
    /// The sprite variant is rolled here, once, the way a blueprint used to roll its sprite.
    /// </summary>
    private void BuildTerrain(NeighborhoodLayout neighborhoodLayout, MathUtility layoutRolls, ushort terrainTypeId, int column, int row, TerrainLayer terrainLayer) =>
        neighborhoodLayout.SetTerrain(column, row, terrainLayer, _terrain.CreateCell(terrainTypeId, layoutRolls));

    /// <summary>A wall is a structure cell, not an entity -- the same flyweight as terrain, on its own MapLayer store.</summary>
    private void BuildWall(NeighborhoodLayout neighborhoodLayout, MathUtility layoutRolls, int column, int row, MapLayer mapLayer) =>
        neighborhoodLayout.SetStructure(new Vector3Int(column, row, (int)mapLayer), _terrain.CreateCell(_stoneWall, layoutRolls));

    /// <summary>Lava 1%, dirt 39%, grass 60% -- a 0-99 roll.</summary>
    private ushort PickGroundTerrain(MathUtility layoutRolls)
    {
        var roll = layoutRolls.Next(0, 100);
        return roll switch
        {
            < 1 => _lava,
            < 40 => _dirt,
            _ => _grass,
        };
    }

    /// <summary>
    /// Standalone demonstration entities, placed individually rather than through the main
    /// population loop above (PopulateEntity). These specifically exercise capabilities
    /// nothing in the main loop touches: multiple components of the same type on one entity
    /// (MultiComponentPool's whole reason for existing), removing a component after blueprint
    /// construction, and text long enough to actually word-wrap/hyphenate when selected.
    /// </summary>
    /// <remarks>
    /// Each one is a blueprint, spawned the same way the bulk population is (see BuildRaceEntity):
    /// what used to be a hand-built entity with components tweaked afterwards is now a race, a class and
    /// a modifier part, so a fixture is reproducible from its spawn record like everything else.
    /// </remarks>
    private void BuildFixtureEntities(Population population)
    {
        // Long description: visually exercises SelectionWindowContent's word-wrap/hyphenation when
        // selected -- the algorithm itself is unit tested, but nothing else on the map has a
        // description long enough to actually wrap or hyphenate.
        SpawnFixture(population, _longDescriptionGoblin, column: 2, row: 2, new Vector2Byte(2, 2));

        // Huge (3x3) goblin engineer, placed standalone rather than through the population rotation.
        SpawnFixture(population, _goblinEngineer, column: 5, row: 5, new Vector2Byte(3, 3));

        // Stationary Fairy engineer: race+class composed, then movement taken away so it doesn't
        // wander despite Fairy's own baseline movement mode.
        SpawnFixture(population, _stationaryFairyEngineer, column: 1, row: 1);

        // Ordinary moving Fairy, for contrast against the stationary one above.
        SpawnFixture(population, _fairy, column: 17, row: 16);

        // Two races in one entity's race slots (Goblin base with Fairy layered on top), stationary
        // since a grounded-goblin/flying-fairy hybrid has no single coherent movement mode.
        SpawnFixture(population, _multiRace, column: 17, row: 9);

        // Two classes in one entity's class slots (Engineer and Tank both applied to the same Goblin).
        SpawnFixture(population, _multiClass, column: 11, row: 2);

        // Tiny-entity occupancy fixtures: 4 partially fill MapWindow's 3x3 tiny grid, 11
        // exercise its 9-entity cap (the extra 2 are built but never drawn).
        SpawnTinyGoblins(population, count: 4, column: 3, row: 5);
        SpawnTinyGoblins(population, count: 11, column: 7, row: 5);

        // Phasing fairy, deliberately co-located with the ordinary moving Fairy above (17,16)
        // -- both are Flying layer, so the Phasing entity overlaps a Blocking one at the same
        // layer, the scenario Occupancy exists to support, rather than relying on a
        // coincidental overlap elsewhere.
        SpawnFixture(population, _phasingFairy, column: 17, row: 16);

        // One of each shop near the player's own TEMPORARY spawn point (SpawnColumn, SpawnRow --
        // see FloorBuilder.PlayerSpawnOrigin); column offsets keep both clear of the column-16 wall
        // corridor and of each other.
        SpawnFixture(population, _generalShop, column: 21, row: SpawnRow);
        SpawnFixture(population, _potionShop, column: 24, row: SpawnRow);

        // A composite of a composite: GoblinEngineer plus Boss. Last, so adding it didn't shift the seeds the fixtures above draw.
        SpawnFixture(population, _goblinForeman, column: 13, row: 5);
    }

    /// <inheritdoc cref="BuildFixtureEntities"/>
    private void SpawnTinyGoblins(Population population, int count, int column, int row)
    {
        for (var i = 0; i < count; i++)
        {
            SpawnFixture(population, _tinyGoblin, column, row);
        }
    }

    /// <summary>Plans one fixture at column/row counted from the starting neighborhood's origin, on its blueprint's own layer, at size or its blueprint's own footprint.</summary>
    private void SpawnFixture(Population population, ushort blueprintId, int column, int row, Vector2Byte? size = null) =>
        population.Spawns.Add(new SpawnRequest(blueprintId, Neighborhoods.OriginOf(StartingCellX) + column, Neighborhoods.OriginOf(StartingCellY) + row)
        {
            Size = size,
            Seed = population.Rolls.NextSeed(),
        });
}
