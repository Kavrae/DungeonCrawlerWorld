using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Utilities;
using Game.Blueprints;
using Game.Blueprints.Classes;
using Game.Blueprints.NPCs.Generic;
using Game.Blueprints.Objects;
using Game.Blueprints.Races;
using Game.Modules.Core.Components;
using Game.Modules.Crawler.Components;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.Terrain;
using Game.World;

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
public sealed class TestMapBuilder(EntityManager entityManager, ComponentManager componentManager, UniqueNumberAllocator crawlerNumberAllocator, FrameEventBuffer<EntityMovedEvent> movedEntities, TerrainRegistry terrain, ProcessingTierResolver? tierResolver = null, SimulationClock? clock = null)
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

    private const string LongWordWrapDescription =
        "ThisIsAReallyLongDescriptionToTestTheWordWrapCapabilitiesAroundHyphenatingLongWordsMultipleTimes";

    private readonly ushort _stoneFloor = terrain.GetId(BuiltInTerrain.StoneFloorKey);
    private readonly ushort _stoneWall = terrain.GetId(BuiltInTerrain.StoneWallKey);
    private readonly ushort _dirt = terrain.GetId(BuiltInTerrain.DirtKey);
    private readonly ushort _lava = terrain.GetId(BuiltInTerrain.LavaKey);
    private readonly ushort _grass = terrain.GetId(BuiltInTerrain.GrassKey);
    private readonly PackedComponentPool<MovementComponent> _movementComponents = componentManager.GetPackedPool<MovementComponent>();
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks = componentManager.GetPackedPool<ActionLockComponent>();

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
    /// <remarks>Every record is assigned before any neighborhood is generated, so the seeds a session's first window gets never depend on how much generation consumed.</remarks>
    public void Populate(World.World world, NeighborhoodRecords records)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(records);

        var bounds = world.Map.Bounds;
        var neighborhoods = new List<NeighborhoodRecord>();
        for (var cellY = Neighborhoods.CellOf(bounds.MinY); cellY <= Neighborhoods.CellOf(bounds.MaxY - 1); cellY++)
        {
            for (var cellX = Neighborhoods.CellOf(bounds.MinX); cellX <= Neighborhoods.CellOf(bounds.MaxX - 1); cellX++)
            {
                if (world.Map.IsNeighborhoodLoaded(cellX, cellY))
                {
                    neighborhoods.Add(records.GetOrCreate(cellX, cellY));
                }
            }
        }

        foreach (var record in neighborhoods)
        {
            foreach (var _ in GenerateNeighborhood(world, record))
            {
            }
        }
    }

    /// <summary>Generates one neighborhood -- every cell's terrain and walls, then its population -- a row at a time.</summary>
    /// <remarks>
    /// GenerateLayout then PopulateNeighborhood, one after the other. Two passes because walls block
    /// placement: a multi-tile creature rolled beside a wall that isn't written yet would otherwise be
    /// placed straddling it.
    /// </remarks>
    public IEnumerable<int> GenerateNeighborhood(World.World world, NeighborhoodRecord record) =>
        GenerateLayout(world, record).Concat(PopulateNeighborhood(world, record));

    /// <summary>Writes one neighborhood's terrain and walls from record.Seed alone, a row at a time.</summary>
    /// <remarks>Yields 0 after each row, so a caller can spread it over frames. Only the part inside the map's bounds is generated.</remarks>
    public IEnumerable<int> GenerateLayout(World.World world, NeighborhoodRecord record)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(record);

        if (!TryGetArea(world, record, out var minX, out var minY, out var maxX, out var maxY))
        {
            yield break;
        }

        var layout = new MathUtility(new Random(record.Seed));
        for (var row = minY; row < maxY; row++)
        {
            for (var column = minX; column < maxX; column++)
            {
                GenerateLayoutCell(world, layout, column, row);
            }

            yield return 0;
        }
    }

    /// <summary>Creates one neighborhood's creatures, and in the starting neighborhood its fixtures and shops, a row at a time.</summary>
    /// <remarks>
    /// Yields after each row with the number of entities it created, so a caller can spread it over
    /// frames. Reads only record.NextPopulationSeed, so the result doesn't depend on what else was
    /// generated first. A creature that can't be placed is destroyed rather than left off the map.
    /// </remarks>
    public IEnumerable<int> PopulateNeighborhood(World.World world, NeighborhoodRecord record)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(record);

        if (!TryGetArea(world, record, out var minX, out var minY, out var maxX, out var maxY))
        {
            yield break;
        }

        var population = new Population(new MathUtility(new Random(record.NextPopulationSeed())), entityManager.Keys);
        for (var row = minY; row < maxY; row++)
        {
            var livingBefore = entityManager.LivingEntityCount;
            for (var column = minX; column < maxX; column++)
            {
                PopulateCell(world, population, column, row);
            }

            yield return entityManager.LivingEntityCount - livingBefore;
        }

        if (record.CellX == StartingCellX && record.CellY == StartingCellY)
        {
            var livingBefore = entityManager.LivingEntityCount;
            BuildFixtureEntities(world, population);
            yield return entityManager.LivingEntityCount - livingBefore;
        }
    }

    /// <summary>The part of record's neighborhood inside the map's bounds, as inclusive minimums and exclusive maximums; false when there is none.</summary>
    private static bool TryGetArea(World.World world, NeighborhoodRecord record, out int minX, out int minY, out int maxX, out int maxY)
    {
        var bounds = world.Map.Bounds;
        minX = System.Math.Max(Neighborhoods.OriginOf(record.CellX), bounds.MinX);
        minY = System.Math.Max(Neighborhoods.OriginOf(record.CellY), bounds.MinY);
        maxX = System.Math.Min(Neighborhoods.OriginOf(record.CellX + 1), bounds.MaxX);
        maxY = System.Math.Min(Neighborhoods.OriginOf(record.CellY + 1), bounds.MaxY);
        return minX < maxX && minY < maxY;
    }

    /// <summary>The blueprints and the random sequence one neighborhood's population is rolled from.</summary>
    private sealed class Population(MathUtility rolls, EntityKeys entityKeys)
    {
        public MathUtility Rolls { get; } = rolls;
        public Goblin Goblin { get; } = new(rolls);
        public Fairy Fairy { get; } = new(rolls);
        public Ghost Ghost { get; } = new(rolls);
        public Engineer Engineer { get; } = new();
        public Tank Tank { get; } = new(entityKeys);
        public GoblinEngineerBlueprint GoblinEngineer { get; } = new(new Goblin(rolls), new Engineer());
        public GeneralShop GeneralShop { get; } = new(rolls);
        public PotionShop PotionShop { get; } = new(rolls);
    }

    /// <summary>
    /// Ground: stone floor under a wall where the starting neighborhood's hallway cross runs, randomized
    /// terrain everywhere else (see PickGroundTerrain). UnderGround: a randomized dirt/lava mixture --
    /// its own independent roll from Ground's mix, so the two layers don't mirror each other.
    /// </summary>
    private void GenerateLayoutCell(World.World world, MathUtility layout, int column, int row)
    {
        if (IsHallwayWall(column, row))
        {
            BuildTerrain(world, layout, _stoneFloor, column, row, TerrainLayer.Ground);
            BuildWall(world, layout, column, row, MapLayer.Ground);
        }
        else
        {
            BuildTerrain(world, layout, PickGroundTerrain(layout), column, row, TerrainLayer.Ground);
        }

        BuildTerrain(world, layout, layout.Next(0, 20) == 0 ? _lava : _dirt, column, row, TerrainLayer.UnderGround);
    }

    private void PopulateCell(World.World world, Population population, int column, int row)
    {
        if (!IsHallwayWall(column, row) && population.Rolls.Next(0, 100) < GroundPopulationPercent)
        {
            PopulateGroundEntity(world, population, column, row);
        }

        if (population.Rolls.Next(0, 100) < UnderGroundGhostPercent)
        {
            PopulateUnderGroundGhost(world, population, column, row);
        }

        // Flying layer: no walls of its own, so every cell is eligible.
        if (population.Rolls.Next(0, 100) < FlyingFairyPercent)
        {
            PopulateFlyingFairy(world, population, column, row);
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
    private void PopulateGroundEntity(World.World world, Population population, int column, int row)
    {
        var roll = population.Rolls.Next(0, 100);
        switch (roll)
        {
            case < 40:
                BuildRaceEntity(world, population, population.Goblin, column, row, new Vector2Byte(1, 1), MapLayer.Ground);
                break;
            case < 48:
                BuildRaceEntity(world, population, population.Goblin, column, row, new Vector2Byte(2, 2), MapLayer.Ground);
                break;
            case < 49:
                BuildRaceEntity(world, population, population.Goblin, column, row, new Vector2Byte(3, 3), MapLayer.Ground);
                break;
            case < 89:
                BuildRaceEntity(world, population, population.Fairy, column, row, new Vector2Byte(1, 1), MapLayer.Ground);
                break;
            case < 97:
                BuildRaceEntity(world, population, population.Fairy, column, row, new Vector2Byte(2, 2), MapLayer.Ground);
                break;
            case < 98:
                BuildRaceEntity(world, population, population.Fairy, column, row, new Vector2Byte(3, 3), MapLayer.Ground);
                break;
            default:
                BuildRaceEntity(world, population, population.Ghost, column, row, new Vector2Byte(1, 2), MapLayer.Ground);
                break;
        }
    }

    /// <summary>UnderGround layer's dedicated Ghost population (see UnderGroundGhostPercent for the gate): 90% 1x1, 9% 2x2, 1% 3x3.</summary>
    private void PopulateUnderGroundGhost(World.World world, Population population, int column, int row)
    {
        var size = population.Rolls.Next(0, 100) switch
        {
            < 90 => new Vector2Byte(1, 1),
            < 99 => new Vector2Byte(2, 2),
            _ => new Vector2Byte(3, 3),
        };

        BuildRaceEntity(world, population, population.Ghost, column, row, size, MapLayer.UnderGround);
    }

    /// <summary>Flying layer's dedicated Fairy population (see FlyingFairyPercent for the gate): 90% 1x1, 9% 2x2, 1% 3x3.</summary>
    private void PopulateFlyingFairy(World.World world, Population population, int column, int row)
    {
        var size = population.Rolls.Next(0, 100) switch
        {
            < 90 => new Vector2Byte(1, 1),
            < 99 => new Vector2Byte(2, 2),
            _ => new Vector2Byte(3, 3),
        };

        BuildRaceEntity(world, population, population.Fairy, column, row, size, MapLayer.Flying);
    }

    /// <summary>Builds a race blueprint entity at the given size/layer with a staggered action lock -- the shared path for every PopulateEntity roll outcome. A small percentage also become Crawlers (see CrawlerPercent).</summary>
    private void BuildRaceEntity(World.World world, Population population, IBlueprint blueprint, int column, int row, Vector2Byte size, MapLayer mapLayer)
    {
        var position = new Vector3Int(column, row, (int)mapLayer);
        var entityId = CreateEntityAt(position);
        blueprint.Build(componentManager, entityId);

        ref var transform = ref componentManager.GetDirectPool<TransformComponent>().Get(entityId);
        transform.Size = size;

        var isCrawler = population.Rolls.Next(0, 100) < CrawlerPercent;
        StaggerActionLock(population, entityId);
        world.PlaceEntityOnMap(entityId, position, ref transform);

        if (!world.IsOnMap(transform.Position))
        {
            entityManager.DestroyEntity(entityId);
            return;
        }

        if (isCrawler)
        {
            componentManager.Merge(entityId, new CrawlerComponent(crawlerNumberAllocator.Allocate()));
        }

        // Spawning counts as a move (see FloorBuilder.CreatePlayer's identical reasoning) so a
        // creature placed directly into a static aura source's range (e.g. spawned beside Lava)
        // is granted immediately, the same as one that later steps into range under its own
        // power -- World.PlaceEntityOnMap itself never raises an EntityMovedEvent, and
        // StatusEffectAuraSystem's own one-time startup scatter (EnsureGrid) only registers
        // SOURCES into the grid, it never grants to occupants already standing in one's radius.
        // Recorded into the shared buffer StatusEffectAuraSystem/ContactDamageSystem actually
        // drain, not published on the bus -- this is bulk population-time placement (tens of
        // thousands of entities per floor), not the rare player-move frequency PlayerActivityLog
        // is built around.
        movedEntities.Record(new EntityMovedEvent(entityId, position, position, transform.Size));
    }

    private void BuildFromBlueprint(World.World world, Population population, IBlueprint blueprint, int column, int row)
    {
        var entityId = entityManager.CreateEntity();
        blueprint.Build(componentManager, entityId);
        StaggerActionLock(population, entityId);

        PlaceAt(world, entityId, column, row);
    }

    /// <summary>
    /// Terrain (the floor an entity stands on) is a cell, not an entity -- see TerrainDefinition.
    /// The sprite variant is rolled here, once, the way a blueprint used to roll its sprite.
    /// </summary>
    private void BuildTerrain(World.World world, MathUtility layout, ushort terrainTypeId, int column, int row, TerrainLayer terrainLayer) =>
        world.PopulateTerrain(column, row, terrainLayer, terrain.CreateCell(terrainTypeId, layout));

    /// <summary>A wall is a structure cell, not an entity -- the same flyweight as terrain, on its own MapLayer store.</summary>
    private void BuildWall(World.World world, MathUtility layout, int column, int row, MapLayer mapLayer) =>
        world.PopulateStructure(new Vector3Int(column, row, (int)mapLayer), terrain.CreateCell(_stoneWall, layout));

    /// <summary>Lava 1%, dirt 39%, grass 60% -- a 0-99 roll.</summary>
    private ushort PickGroundTerrain(MathUtility layout)
    {
        var roll = layout.Next(0, 100);
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
    private void BuildFixtureEntities(World.World world, Population population)
    {
        // Long description: visually exercises SelectionWindowContent's word-wrap/
        // hyphenation when selected -- the algorithm itself is unit tested, but nothing
        // else on the map has a description long enough to actually wrap or hyphenate.
        var longDescriptionId = entityManager.CreateEntity();
        population.Goblin.Build(componentManager, longDescriptionId);

        ref var longDescriptionText = ref componentManager.GetDirectPool<DisplayTextComponent>().Get(longDescriptionId);
        longDescriptionText.Description = LongWordWrapDescription;

        ref var longDescriptionTransform = ref componentManager.GetDirectPool<TransformComponent>().Get(longDescriptionId);
        longDescriptionTransform.Size = new Vector2Byte(2, 2);

        StaggerActionLock(population, longDescriptionId);
        PlaceAt(world, longDescriptionId, 2, 2);

        // Huge (3x3) goblin engineer, placed standalone rather than through BuildGoblin's rotation.
        var hugeId = entityManager.CreateEntity();
        population.GoblinEngineer.Build(componentManager, hugeId);

        ref var hugeTransform = ref componentManager.GetDirectPool<TransformComponent>().Get(hugeId);
        hugeTransform.Size = new Vector2Byte(3, 3);

        StaggerActionLock(population, hugeId);
        PlaceAt(world, hugeId, 5, 5);

        // Stationary Fairy engineer: race+class composed, then MovementComponent removed so
        // it doesn't wander despite Fairy's own baseline movement mode.
        var stationaryFairyId = entityManager.CreateEntity();
        population.Fairy.Build(componentManager, stationaryFairyId);
        population.Engineer.Build(componentManager, stationaryFairyId);
        _movementComponents.Remove(stationaryFairyId);

        PlaceAt(world, stationaryFairyId, 1, 1);

        // Ordinary moving Fairy, for contrast against the stationary one above.
        BuildFromBlueprint(world, population, population.Fairy, 17, 16);

        // Two RaceComponents on one entity (Goblin base with Fairy layered on top). Movement
        // removed since a grounded-goblin/flying-fairy hybrid has no single coherent
        // movement mode.
        var multiRaceId = entityManager.CreateEntity();
        population.Goblin.Build(componentManager, multiRaceId);
        population.Fairy.Build(componentManager, multiRaceId);
        _movementComponents.Remove(multiRaceId);

        PlaceAt(world, multiRaceId, 17, 9);

        // Two ClassComponents on one entity (Engineer and Tank both applied to the same Goblin).
        var multiClassId = entityManager.CreateEntity();
        population.Goblin.Build(componentManager, multiClassId);
        population.Engineer.Build(componentManager, multiClassId);
        population.Tank.Build(componentManager, multiClassId);

        StaggerActionLock(population, multiClassId);
        PlaceAt(world, multiClassId, 11, 2);

        // Tiny-entity occupancy fixtures: 4 partially fill MapWindow's 3x3 tiny grid, 11
        // exercise its 9-entity cap (the extra 2 are built but never drawn).
        BuildTinyGoblins(world, population, count: 4, column: 3, row: 5);
        BuildTinyGoblins(world, population, count: 11, column: 7, row: 5);

        // Phasing fairy, deliberately co-located with the ordinary moving Fairy above (17,16)
        // -- both are Flying layer, so the Phasing entity overlaps a Blocking one at the same
        // layer, the scenario Occupancy exists to support, rather than relying on a
        // coincidental overlap elsewhere.
        var phasingFairyId = entityManager.CreateEntity();
        population.Fairy.Build(componentManager, phasingFairyId); // Fairy's own blueprint already includes MovementComponent.
        componentManager.GetMultiPool<NonBlockingComponent>().Add(phasingFairyId, new NonBlockingComponent(NonBlockingKind.Phasing));

        StaggerActionLock(population, phasingFairyId);
        PlaceAt(world, phasingFairyId, 17, 16);

        // One of each shop near the player's own TEMPORARY spawn point (SpawnColumn, SpawnRow --
        // see FloorBuilder.PlayerSpawnOrigin); column offsets keep both clear of the column-16 wall
        // corridor and of each other.
        BuildShop(world, population.GeneralShop, column: 21, row: SpawnRow);
        BuildShop(world, population.PotionShop, column: 24, row: SpawnRow);
    }

    private void BuildShop(World.World world, IBlueprint shop, int column, int row)
    {
        var entityId = entityManager.CreateEntity();
        shop.Build(componentManager, entityId);
        PlaceAt(world, entityId, column, row);
    }

    /// <summary>Builds count plain-Goblin-glyph entities, all Tiny, all at the same cell -- for exercising MapWindow's tiny-entity grid/cap.</summary>
    private void BuildTinyGoblins(World.World world, Population population, int count, int column, int row)
    {
        for (var i = 0; i < count; i++)
        {
            var entityId = entityManager.CreateEntity();
            population.Goblin.Build(componentManager, entityId);
            componentManager.GetMultiPool<NonBlockingComponent>().Add(entityId, new NonBlockingComponent(NonBlockingKind.Tiny));

            StaggerActionLock(population, entityId);
            PlaceAt(world, entityId, column, row);
        }
    }

    private static readonly ushort MaximumStaggerFrames = GameTiming.FramesForSeconds(1f);

    /// <summary>
    /// Randomizes a freshly-built goblin/fairy's starting action lock to a value between 0 and
    /// MaximumStaggerFrames, instead of the 0 every race blueprint merges by default -- without
    /// this, an entire periodic population spawns ready to act on the same handful of frames
    /// and visibly moves in lockstep bursts rather than spreading out over time. The exact
    /// upper bound doesn't need to track any entity's real StandardLockFrames -- this only
    /// matters for the initial stagger, and gets fully overwritten the first time the entity
    /// actually moves (ActionLockGate.Lock sets both fields together at that point).
    /// </summary>
    private void StaggerActionLock(Population population, int entityId)
    {
        var framesToWait = (ushort)population.Rolls.Next(0, MaximumStaggerFrames + 1);

        ActionLockGate.Lock(_actionLocks, entityId, now: clock?.CurrentFrame ?? 0, framesToWait);
    }

    /// <summary>
    /// Creates an entity born with its processing tier as its first component, for the position it
    /// is about to be placed at -- see ProcessingTierResolver.CreateEntityAt. Used by the bulk path
    /// whose final position is known before the blueprint is built (race entities), which is nearly
    /// every entity and so where silent tier-first assignment pays for itself.
    /// </summary>
    /// <remarks>
    /// The PlaceAt paths below (fixtures, shops, tiny goblins) deliberately still use
    /// a plain CreateEntity: their Z comes from the blueprint (PlaceAt keeps whatever
    /// transform.Position.Z the blueprint set), so the final position is not known until after
    /// Build. They are tiered by World.EntityPlaced -> ProcessingTierResolver.EnsureTiered instead,
    /// which raises a TierChanged per entity -- a few thousand events at population, against the
    /// hundred thousand or so tier-first avoids. Without a resolver (unit tests), this is a plain CreateEntity.
    /// </remarks>
    private int CreateEntityAt(Vector3Int plannedPosition) =>
        tierResolver?.CreateEntityAt(entityManager, plannedPosition) ?? entityManager.CreateEntity();

    /// <summary>Places an already-built fixture at column/row counted from the starting neighborhood's origin, preserving the Z height (map layer) its blueprint already set -- a blueprint's own X/Y is just a placeholder. A fixture that can't be placed is destroyed, like a creature.</summary>
    private void PlaceAt(World.World world, int entityId, int column, int row)
    {
        ref var transform = ref componentManager.GetDirectPool<TransformComponent>().Get(entityId);
        var position = new Vector3Int(Neighborhoods.OriginOf(StartingCellX) + column, Neighborhoods.OriginOf(StartingCellY) + row, transform.Position.Z);

        world.PlaceEntityOnMap(entityId, position, ref transform);

        if (!world.IsOnMap(transform.Position))
        {
            entityManager.DestroyEntity(entityId);
        }
    }
}
