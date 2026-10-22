using Engine.ECS.Entities;
using Engine.Math;
using Game.Areas;
using Game.Blueprints.Objects;
using Game.Blueprints.Races;
using Game.Floors;
using Game.Spawning;
using Game.Modules.Auras;
using Game.Modules.Core.Components;
using Game.Terrain;
using Game.World;
using Game.Blueprints;

namespace Game;

/// <summary>
/// Builds test neighborhoods across all three MapLayers, each layer with its own independent,
/// percentage-rolled population: Ground (randomized lava/dirt/grass terrain, a Healing Shrine on about one cell in a thousand, a Goblin/Fairy/Ghost on
/// GroundPopulationPercent of free tiles per PopulateGroundEntity's breakdown), UnderGround (a
/// randomized dirt/lava mixture, a Ghost on UnderGroundGhostPercent of tiles per
/// PopulateUnderGroundGhost), and Flying (a Fairy on FlyingFairyPercent of tiles per
/// PopulateFlyingFairy) -- plus the areas the content places in the neighborhood (AreaPlacement), stamped
/// over the generated layout: the starting area's hallway cross, fixtures and shops in neighborhood (0, 0).
/// </summary>
/// <remarks>
/// A stand-in for real template generation. Each neighborhood is
/// generated from its own NeighborhoodRecord, never from shared random state, and the world has no
/// border walls: it has no edge.
/// </remarks>
public sealed class TestMapBuilder
{
    // TEMPORARY: halved once already from the original values (10/5/5) to reduce the creature
    // population -- Movement/HealthRegen/TerrainContacts/Auras all iterate this
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

    /// <summary>A Healing Shrine stands on one Ground cell in this many.</summary>
    private const int GroundCellsPerHealingShrine = 1000;

    /// <summary>Mixed into a neighborhood's layout seed for the sequence its shrines are rolled from.</summary>
    private const int HealingShrineSeedSalt = 0x5348524E;

    /// <summary>How many shrines are spawned between yields, about a row of creatures' worth, so a neighborhood's shrines spread over frames the way its rows do.</summary>
    private const int HealingShrinesPerSpawnBatch = 64;

    /// <summary>Chance any given rolled NPC (see BuildRaceEntity) is also a Crawler -- deliberately small; most NPCs are not.</summary>
    private const int CrawlerPercent = 2;

    private readonly EntityManager _entityManager;
    private readonly EntityFactory _factory;
    private readonly TerrainRegistry _terrain;
    private readonly AuraCatalog _auras;
    private readonly UniqueEntityRoster _uniqueEntities;
    private readonly AreaStamp _areaStamp;

    /// <summary>Each neighborhood's fixed area placements, with the area each places, in catalog order.</summary>
    private readonly Dictionary<(int CellX, int CellY), List<(AreaPlacement Placement, AreaDefinition Area)>> _fixedPlacementsByNeighborhood = [];

    private readonly ushort _dirt;
    private readonly ushort _lava;
    private readonly ushort _grass;
    private readonly ushort _goblin;
    private readonly ushort _fairy;
    private readonly ushort _ghost;
    private readonly ushort _healingShrine;

    /// <param name="areas">The session's areas: each neighborhood stamps the ones its fixed placements put there.</param>
    /// <param name="uniqueEntities">Asked, as each unique placement spawns, whether its blueprint may.</param>
    public TestMapBuilder(EntityManager entityManager, EntityFactory factory, TerrainRegistry terrain, AuraCatalog auras, BlueprintRegistry definitions, AreaCatalog areas, UniqueEntityRoster uniqueEntities)
    {
        _entityManager = entityManager;
        _factory = factory;
        _terrain = terrain;
        _auras = auras;
        _uniqueEntities = uniqueEntities;
        _areaStamp = new AreaStamp(areas, terrain, definitions);

        _dirt = terrain.GetId(BuiltInTerrain.DirtKey);
        _lava = terrain.GetId(BuiltInTerrain.LavaKey);
        _grass = terrain.GetId(BuiltInTerrain.GrassKey);
        _goblin = definitions.GetId(Goblin.Id);
        _fairy = definitions.GetId(Fairy.Id);
        _ghost = definitions.GetId(Ghost.Id);
        _healingShrine = definitions.GetId(HealingShrine.Id);

        foreach (var placement in areas.Placements.Definitions)
        {
            var neighborhood = (placement.NeighborhoodX, placement.NeighborhoodY);
            if (!_fixedPlacementsByNeighborhood.TryGetValue(neighborhood, out var placements))
            {
                placements = [];
                _fixedPlacementsByNeighborhood[neighborhood] = placements;
            }

            placements.Add((placement, areas.Areas.Get(placement.Area)));
        }
    }

    /// <summary>
    /// Generates every loaded neighborhood, in row-major order. World is built by the
    /// caller (not here) because it must exist before MovementModule -- itself a constructor
    /// dependency of the module build, which is what produces the EntityManager/ComponentManager
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
        var neighborhoodsToGenerate = AssignRecords(LoadedNeighborhoods(world.Map), records);

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

    /// <summary>Every neighborhood map has loaded, in the row-major order Populate generates them in.</summary>
    public static IReadOnlyList<(int CellX, int CellY)> LoadedNeighborhoods(Map map)
    {
        var mapBounds = map.Bounds;
        var cells = new List<(int CellX, int CellY)>();
        for (var cellY = Neighborhoods.CellOf(mapBounds.MinY); cellY <= Neighborhoods.CellOf(mapBounds.MaxY - 1); cellY++)
        {
            for (var cellX = Neighborhoods.CellOf(mapBounds.MinX); cellX <= Neighborhoods.CellOf(mapBounds.MaxX - 1); cellX++)
            {
                if (map.IsNeighborhoodLoaded(cellX, cellY))
                {
                    cells.Add((cellX, cellY));
                }
            }
        }

        return cells;
    }

    /// <summary>Each cell's record, assigned in cells' order if it has none, and its next population seed -- every record assigned before any neighborhood is generated, as Populate does.</summary>
    public static List<(NeighborhoodRecord Record, int PopulationSeed)> AssignRecords(IEnumerable<(int CellX, int CellY)> cells, NeighborhoodRecords records)
    {
        var assigned = new List<(NeighborhoodRecord Record, int PopulationSeed)>();
        foreach (var (cellX, cellY) in cells)
        {
            var neighborhoodRecord = records.GetOrCreate(cellX, cellY);
            assigned.Add((neighborhoodRecord, neighborhoodRecord.NextPopulationSeed()));
        }

        return assigned;
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

    /// <summary>Decides one neighborhood -- its terrain, walls, areas and shrines from record.Seed, its creatures from populationSeed -- without touching the world: safe on a worker thread.</summary>
    /// <remarks>
    /// Reads only the record's seed, the map's fixed shape (Map.CreateLayout) and what this builder
    /// resolved at construction, so the plan doesn't depend on what else was generated first, or on
    /// which thread made it. The layout is generated whole, then the neighborhood's fixed area
    /// placements are stamped over it, before any population, so a creature is always placed against
    /// the walls it will actually stand beside. Random population and shrines stay out of every cell an
    /// area defines; the areas' own spawns are a batch of their own, after the shrines and before the
    /// creatures.
    /// </remarks>
    /// <param name="planningCancellation">Checked once a row: a plan no longer wanted stops early and throws OperationCanceledException.</param>
    public NeighborhoodPlan Plan(Map map, NeighborhoodRecord record, int populationSeed, CancellationToken planningCancellation = default)
    {
        var neighborhoodLayout = map.CreateLayout(record.CellX, record.CellY, record.Seed);
        var layoutRolls = new MathUtility(new SeededRandom((ulong)(uint)record.Seed));
        for (var row = neighborhoodLayout.MinY; row < neighborhoodLayout.MaxY; row++)
        {
            planningCancellation.ThrowIfCancellationRequested();
            for (var column = neighborhoodLayout.MinX; column < neighborhoodLayout.MaxX; column++)
            {
                GenerateLayoutCell(neighborhoodLayout, layoutRolls, column, row);
            }
        }

        var areas = new AreaStampResult();
        if (_fixedPlacementsByNeighborhood.TryGetValue((record.CellX, record.CellY), out var fixedPlacements))
        {
            foreach (var (placement, area) in fixedPlacements)
            {
                _areaStamp.Apply(neighborhoodLayout, AreaInstance.ForFixedPlacement(placement, area, record.Seed), areas);
            }
        }

        var population = new Population(new MathUtility(new SeededRandom((ulong)(uint)populationSeed)));
        var spawnRowEnds = new List<int>(neighborhoodLayout.MaxY - neighborhoodLayout.MinY + 2);
        PlanHealingShrines(population, spawnRowEnds, neighborhoodLayout, record, areas, planningCancellation);

        var uniqueSpawns = PlanAreaSpawns(population, spawnRowEnds, areas);

        for (var row = neighborhoodLayout.MinY; row < neighborhoodLayout.MaxY; row++)
        {
            planningCancellation.ThrowIfCancellationRequested();
            for (var column = neighborhoodLayout.MinX; column < neighborhoodLayout.MaxX; column++)
            {
                PopulateCell(population, areas, column, row);
            }

            spawnRowEnds.Add(population.Spawns.Count);
        }

        return new NeighborhoodPlan(neighborhoodLayout, TerrainAuraSources.ByRow(neighborhoodLayout, _terrain, _auras), population.Spawns, spawnRowEnds, areas, uniqueSpawns);
    }

    /// <summary>Spawns neighborhoodPlan's shrines, areas' residents and creatures in order, a batch at a time, once its layout is loaded. Main thread only.</summary>
    /// <remarks>
    /// Yields after each batch (a batch of shrines, the areas' spawns, then a row of creatures) with the number of
    /// entities it created, so a caller can spread it over frames. A creature that can't be placed is destroyed rather
    /// than left off the map. A unique placement asks the roster at its turn: its blueprint if none lives and none was
    /// killed this session, else its fallback, if any.
    /// </remarks>
    public IEnumerable<int> Spawn(NeighborhoodPlan neighborhoodPlan)
    {
        var nextSpawnIndex = 0;
        foreach (var spawnRowEnd in neighborhoodPlan.SpawnRowEnds)
        {
            var livingBefore = _entityManager.LivingEntityCount;
            for (; nextSpawnIndex < spawnRowEnd; nextSpawnIndex++)
            {
                if (neighborhoodPlan.UniqueSpawns.TryGetValue(nextSpawnIndex, out var uniqueSpawn))
                {
                    AreaSpawner.Spawn(_factory, _uniqueEntities, uniqueSpawn);
                    continue;
                }

                _factory.Spawn(neighborhoodPlan.Spawns[nextSpawnIndex]);
            }

            yield return _entityManager.LivingEntityCount - livingBefore;
        }
    }

    /// <summary>The random sequence one neighborhood's population is rolled from, and the spawns it has rolled so far.</summary>
    private sealed class Population(MathUtility rolls)
    {
        public MathUtility Rolls { get; } = rolls;

        public List<SpawnRequest> Spawns { get; } = [];
    }

    /// <summary>Ground: randomized terrain (see PickGroundTerrain). UnderGround: a randomized dirt/lava mixture -- its own independent roll from Ground's mix, so the two layers don't mirror each other.</summary>
    /// <remarks>Every cell is rolled, an area's included, so stamping an area over the layout shifts no other cell's terrain.</remarks>
    private void GenerateLayoutCell(NeighborhoodLayout neighborhoodLayout, MathUtility layoutRolls, int column, int row)
    {
        BuildTerrain(neighborhoodLayout, PickGroundTerrain(layoutRolls), column, row, TerrainLayer.Ground);
        BuildTerrain(neighborhoodLayout, layoutRolls.Next(0, 20) == 0 ? _lava : _dirt, column, row, TerrainLayer.UnderGround);
    }

    /// <summary>Plans the neighborhood's Healing Shrines: one Ground cell in GroundCellsPerHealingShrine, anywhere an area doesn't define -- lava included.</summary>
    /// <remarks>
    /// <para>
    /// Rolled from the neighborhood's layout seed, not its population seed: a shrine is part of the
    /// place, so it is in the same cell on every visit, while the creatures around it are rolled anew.
    /// Its own sequence, so it shifts no creature's roll; every cell is rolled, an area's included.
    /// </para>
    /// <para>
    /// All of them are spawned before any creature, in batches of HealingShrinesPerSpawnBatch. First,
    /// so a creature rolled onto a shrine's cell is the one that doesn't fit. Together, so their
    /// entity ids are consecutive: what only a shrine holds (its aura source, its immunities) then
    /// fills a page or two of each pool's entity index per neighborhood. Spawned a row at a time
    /// among the creatures, they touched every page -- 31 MB of pools for 9,000 shrines.
    /// </para>
    /// </remarks>
    private void PlanHealingShrines(Population population, List<int> spawnRowEnds, NeighborhoodLayout neighborhoodLayout, NeighborhoodRecord record, AreaStampResult areas, CancellationToken planningCancellation)
    {
        var shrineRolls = new MathUtility(new SeededRandom((ulong)(uint)(record.Seed ^ HealingShrineSeedSalt)));
        var batchStart = population.Spawns.Count;

        for (var row = neighborhoodLayout.MinY; row < neighborhoodLayout.MaxY; row++)
        {
            planningCancellation.ThrowIfCancellationRequested();
            for (var column = neighborhoodLayout.MinX; column < neighborhoodLayout.MaxX; column++)
            {
                var isShrineCell = shrineRolls.Next(0, GroundCellsPerHealingShrine) == 0;
                if (!isShrineCell || areas.Covers(column, row, MapLayer.Ground))
                {
                    continue;
                }

                population.Spawns.Add(new SpawnRequest(_healingShrine, column, row) { Layer = MapLayer.Ground, Seed = shrineRolls.NextSeed() });
                if (population.Spawns.Count - batchStart == HealingShrinesPerSpawnBatch)
                {
                    spawnRowEnds.Add(population.Spawns.Count);
                    batchStart = population.Spawns.Count;
                }
            }
        }

        if (population.Spawns.Count > batchStart)
        {
            spawnRowEnds.Add(population.Spawns.Count);
        }
    }

    /// <summary>The areas' spawns as one batch, after the shrines and before the creatures, so an area's residents take their cells first; returns those of unique placements by their index.</summary>
    private static Dictionary<int, AreaPlannedSpawn> PlanAreaSpawns(Population population, List<int> spawnRowEnds, AreaStampResult areas)
    {
        var uniqueSpawns = new Dictionary<int, AreaPlannedSpawn>();
        if (areas.Spawns.Count == 0)
        {
            return uniqueSpawns;
        }

        foreach (var areaSpawn in areas.Spawns)
        {
            if (areaSpawn.UniqueBlueprint is not null)
            {
                uniqueSpawns[population.Spawns.Count] = areaSpawn;
            }

            population.Spawns.Add(areaSpawn.Request);
        }

        spawnRowEnds.Add(population.Spawns.Count);
        return uniqueSpawns;
    }

    /// <summary>Each layer's population roll, skipped where an area defines the cell on that layer.</summary>
    private void PopulateCell(Population population, AreaStampResult areas, int column, int row)
    {
        if (!areas.Covers(column, row, MapLayer.Ground) && population.Rolls.Next(0, 100) < GroundPopulationPercent)
        {
            PopulateGroundEntity(population, column, row);
        }

        if (!areas.Covers(column, row, MapLayer.UnderGround) && population.Rolls.Next(0, 100) < UnderGroundGhostPercent)
        {
            PopulateUnderGroundGhost(population, column, row);
        }

        // Flying layer: no walls of its own, so every cell an area leaves alone is eligible.
        if (!areas.Covers(column, row, MapLayer.Flying) && population.Rolls.Next(0, 100) < FlyingFairyPercent)
        {
            PopulateFlyingFairy(population, column, row);
        }
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
    private static void BuildRaceEntity(Population population, ushort blueprintId, int column, int row, Vector2Byte size, MapLayer mapLayer)
    {
        var seed = population.Rolls.NextSeed();
        var isCrawler = population.Rolls.Next(0, 100) < CrawlerPercent;

        population.Spawns.Add(new SpawnRequest(blueprintId, column, row) { Layer = mapLayer, Size = size, Seed = seed, Crawler = isCrawler });
    }

    /// <summary>
    /// Terrain (the floor an entity stands on) is a cell, not an entity -- see TerrainDefinition.
    /// How it looks is picked from the cell's seed (CellSeeds), never rolled here, so the layout doesn't depend on the art.
    /// </summary>
    private static void BuildTerrain(NeighborhoodLayout neighborhoodLayout, ushort terrainTypeId, int column, int row, TerrainLayer terrainLayer) =>
        neighborhoodLayout.SetTerrain(column, row, terrainLayer, new TerrainCell(terrainTypeId));

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
}
