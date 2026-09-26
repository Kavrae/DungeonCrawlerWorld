using Engine.ECS.Entities;
using Engine.Bootstrap;
using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Spawning;
using Game.Floors;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.Containers;
using Game.Modules.Actions;
using Game.Modules.Actions.Definitions;
using Game.Modules.Burning;
using Game.Modules.Class;
using Game.Modules.ContactDamage;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Crawler;
using Game.Modules.Crawler.Components;
using Game.Modules.Currency;
using Game.Modules.Health;
using Game.Modules.Inventory;
using Game.Modules.Mana;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.Modules.NpcBehavior;
using Game.Modules.Poison;
using Game.Modules.ProcessingTier;
using Game.Modules.Race;
using Game.Modules.Shops;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffectAura;
using Game.Modules.StatusEffects;
using Game.Terrain;
using Game.World;
using Game.Blueprints;

namespace Tests.Floors;

[TestClass]
public sealed class FloorBuilderTests
{
    private static EcsContext BuildEcsContext(Game.World.World world, MathUtility mathUtility) => BuildEcsContext(world, mathUtility, out _);

    private static EcsContext BuildEcsContext(Game.World.World world, MathUtility mathUtility, out GameModuleContext context)
    {
        var eventBus = new EventBus();
        context = new GameModuleContext(world, mathUtility, eventBus) { PlayerQuery = world, EntityMoveSync = new WorldEventSync(world) };
        new TerrainModule().Configure(context);
        world.Terrain = context.Terrain;

        var movementModule = new MovementModule();
        movementModule.Configure(context);

        var actionsModule = new ActionsModule();
        actionsModule.Configure(context);

        var coreActionsModule = new CoreActionsModule();
        coreActionsModule.Configure(context);

        var burningModule = new BurningModule();
        burningModule.Configure(context);

        var poisonModule = new PoisonModule();
        poisonModule.Configure(context);

        var contactDamageModule = new ContactDamageModule();
        contactDamageModule.Configure(context);

        var statusEffectAuraModule = new StatusEffectAuraModule();
        statusEffectAuraModule.Configure(context);

        var processingTierModule = new ProcessingTierModule();
        processingTierModule.Configure(context);

        var coreModule = new CoreModule();
        coreModule.Configure(context);

        var healthModule = new HealthModule();
        healthModule.Configure(context);

        var manaModule = new ManaModule();
        manaModule.Configure(context);

        var statModifiersModule = new StatModifiersModule();
        statModifiersModule.Configure(context);

        var abilityScoresModule = new AbilityScoresModule();
        abilityScoresModule.Configure(context);

        var coreItemsModule = new CoreItemsModule();
        coreItemsModule.Configure(context);

        var statusEffectsModule = new StatusEffectsModule();

        var containersModule = new ContainersModule();
        containersModule.Configure(context);

        var shopModule = new ShopModule();
        shopModule.Configure(context);

        var npcBehaviorModule = new NpcBehaviorModule();
        npcBehaviorModule.Configure(context);

        var creatureModule = new BlueprintsModule();
        creatureModule.Configure(context);

        IReadOnlyList<IModule> modules =
        [
            coreModule,
            healthModule,
            manaModule,
            statModifiersModule,
            abilityScoresModule,
            movementModule,
            new RaceModule(),
            new ClassModule(),
            creatureModule,
            actionsModule,
            coreActionsModule,
            statusEffectsModule,
            burningModule,
            poisonModule,
            contactDamageModule,
            statusEffectAuraModule,
            new CrawlerModule(),
            processingTierModule,
            new InventoryModule(),
            coreItemsModule,
            new CurrencyModule(),
            containersModule,
            shopModule,
            npcBehaviorModule,
        ];

        return Bootstrapper.Build(modules, initialEntityCapacity: 5000, initialComponentCapacity: 5000, entityKeys: context.EntityKeys);
    }

    /// <summary>The one spawn path, for a context these tests assembled themselves -- GameBootstrapper builds the real session's (see GameBootstrapResult.Factory).</summary>
    private static EntityFactory FactoryFor(Game.World.World world, EcsContext ecsContext, GameModuleContext context, MathUtility mathUtility, ProcessingTierResolver? tierResolver = null) =>
        new(context.Definitions, world, ecsContext.EntityManager, ecsContext.ComponentManager, context.MovedEntities, tierResolver, ecsContext.SystemManager.Clock, new UniqueNumberAllocator(mathUtility, 1, 13_000_000));

    /// <summary>
    /// The player must not be placed before/during TestMapBuilder.Populate (PlaceEntityOnMap
    /// has no free-space check, so an earlier player placement could be silently overwritten
    /// by a later wall/creature at the same cell) -- this confirms the player actually lands
    /// on a real, unoccupied, on-map cell once CreatePlayer runs (its id reserved separately
    /// via ReservePlayerEntity, before PopulateFloor -- see FloorBuilder's own doc comments for
    /// why: CreatePlayer's free-cell search needs the floor already populated, while reserving
    /// the id first is what lands the player on entity id 0), and that World.PlayerEntityId is
    /// wired to whatever id the player actually got (not any particular hardcoded value here --
    /// see ReservePlayerEntity_CalledFirst_ReturnsEntityIdZero below for that specific claim).
    /// </summary>
    [TestMethod]
    public void PopulateFloor_PlacesPlayerOnAFreeOnMapCellAndWiresPlayerEntityId()
    {
        var world = new Game.World.World(new Map(new Vector3Int(20, 20, 3)));
        var mathUtility = new MathUtility(new Random(1));
        var ecsContext = BuildEcsContext(world, mathUtility, out var context);

        var playerEntityId = FloorBuilder.ReservePlayerEntity(ecsContext);
        var factory = FactoryFor(world, ecsContext, context, mathUtility);
        FloorBuilder.PopulateFloor(world, ecsContext, new NeighborhoodRecords(mathUtility), factory, context.Terrain, context.Definitions);
        FloorBuilder.CreatePlayer(world, ecsContext, mathUtility, factory, context.Definitions, playerEntityId);
        world.PlayerEntityId = playerEntityId;

        Assert.IsTrue(ecsContext.EntityManager.EntityExists(world.PlayerEntityId));

        var transform = ecsContext.ComponentManager.GetDirectPool<TransformComponent>().GetReadonly(world.PlayerEntityId);
        Assert.IsTrue(world.IsOnMap(transform.Position));
        Assert.AreEqual(world.PlayerEntityId, world.GetEntityIdAt(transform.Position));

        var movement = ecsContext.ComponentManager.GetPackedPool<MovementComponent>().GetReadonly(world.PlayerEntityId);
        Assert.AreEqual(MovementMode.PlayerControlled, movement.MovementMode);

        // The player is always a Crawler, numbered by the session rather than by what it is built from.
        Assert.IsTrue(ecsContext.ComponentManager.GetPackedPool<CrawlerComponent>().Has(world.PlayerEntityId));
    }

    [TestMethod]
    [DataRow(null, -1024, 2048)]
    [DataRow(3072, -1024, 2048)]
    [DataRow(1024, 0, 1024)]
    [DataRow(2000, -1024, 976)]
    public void CreateMap_IsCentredOnNeighborhoodZero(int? sizeOverride, int expectedMin, int expectedMax) =>
        Assert.AreEqual(new MapBounds(expectedMin, expectedMin, expectedMax, expectedMax, 3), FloorBuilder.CreateMap(floorNumber: 1, sizeOverride).Bounds);

    /// <summary>A creature whose footprint can't be placed (a 3x3 rolled beside another creature) is destroyed, not left existing off the map. Neighborhood -1 only, so no hand-placed fixture is involved.</summary>
    [TestMethod]
    public void PopulateFloor_EveryCreatureCreatedIsOnTheMap()
    {
        var world = new Game.World.World(new Map(new MapBounds(-1024, 0, 0, 1024, 3)));
        var mathUtility = new MathUtility(new Random(1));
        var ecsContext = BuildEcsContext(world, mathUtility, out var context);

        FloorBuilder.PopulateFloor(world, ecsContext, new NeighborhoodRecords(mathUtility), FactoryFor(world, ecsContext, context, mathUtility), context.Terrain, context.Definitions);

        var transforms = ecsContext.ComponentManager.GetDirectPool<TransformComponent>();
        var creatures = 0;
        for (var entityId = 0; entityId < transforms.Capacity; entityId++)
        {
            if (transforms.TryGetReadonly(entityId, out var transform))
            {
                creatures++;
                Assert.IsTrue(IsIndexedAt(world, entityId, transform.Position), $"Entity {entityId} exists but isn't on the map.");
            }
        }

        Assert.IsGreaterThan(1_000, creatures, "Precondition: a real population.");
        Assert.AreEqual(creatures, ecsContext.EntityManager.LivingEntityCount);
    }

    [TestMethod]
    public void PopulateFloor_WallsAreStructuresAndNoOccupantStandsInOne()
    {
        var world = new Game.World.World(new Map(new Vector3Int(40, 40, 3)));
        var mathUtility = new MathUtility(new Random(1));
        var ecsContext = BuildEcsContext(world, mathUtility, out var context);

        FloorBuilder.PopulateFloor(world, ecsContext, new NeighborhoodRecords(mathUtility), FactoryFor(world, ecsContext, context, mathUtility), context.Terrain, context.Definitions);

        var wallId = context.Terrain.GetId(BuiltInTerrain.StoneWallKey);
        Assert.AreEqual(wallId, world.GetStructureAt(new Vector3Int(10, 2, (int)MapLayer.Ground)).TypeId);
        Assert.IsTrue(world.GetStructureAt(new Vector3Int(0, 5, (int)MapLayer.Ground)).IsEmpty, "No border walls: the world has no edge.");
        Assert.IsTrue(world.GetStructureAt(new Vector3Int(0, 5, (int)MapLayer.UnderGround)).IsEmpty);

        for (var z = 0; z < 3; z++)
        {
            for (var y = 0; y < 40; y++)
            {
                for (var x = 0; x < 40; x++)
                {
                    var position = new Vector3Int(x, y, z);
                    if (!world.IsCellBlocked(position))
                    {
                        continue;
                    }

                    foreach (var occupant in world.GetOccupantEntityIdsAt(position))
                    {
                        Assert.IsTrue(world.IsPhasing(occupant), $"Entity {occupant} stands in the wall at {position}.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// The actual point of ReservePlayerEntity existing as a separate, earlier call: reserving
    /// before PopulateFloor's NPC and wall entities exist lands the player on entity id 0
    /// (FreeIdPool.Rent's first call against a fresh pool always returns 0), not a high id
    /// assigned after population -- see ReservePlayerEntity's own doc comment for why that
    /// matters (Player-only component pool capacity).
    /// </summary>
    [TestMethod]
    public void ReservePlayerEntity_CalledFirst_ReturnsEntityIdZero()
    {
        var world = new Game.World.World(new Map(new Vector3Int(20, 20, 3)));
        var mathUtility = new MathUtility(new Random(1));
        var ecsContext = BuildEcsContext(world, mathUtility);

        var playerEntityId = FloorBuilder.ReservePlayerEntity(ecsContext);

        Assert.AreEqual(0, playerEntityId);
    }

    /// <summary>
    /// End-to-end invariant for the tier rework, through the real population path: with the tier
    /// reference set to the player's spawn origin before population, every entity the map actually
    /// indexes -- movers, shops -- is born with the tier its position deserves, and
    /// stationary entities in particular are no longer left at the fail-open Beyond default (the
    /// bug the rework exists to fix: before it, only movers were ever tiered).
    /// </summary>
    /// <remarks>
    /// "Indexed by the map" rather than "has a TransformComponent": a multi-tile NPC whose placement
    /// fails (overlap) keeps its blueprint's placeholder position and is not on the map at all, so
    /// holding it to the invariant would test TestMapBuilder's overlap handling, not tiering. The map
    /// is wide enough (200 columns from a spawn origin at column 17) that part of it is beyond the
    /// Local radius, so Neighborhood is exercised too, not just Local vs Beyond-by-layer.
    /// </remarks>
    [TestMethod]
    public void PopulateFloor_WithTierResolver_EveryIndexedEntityIsBornCorrectlyTiered()
    {
        var world = new Game.World.World(new Map(new Vector3Int(200, 20, 3)));
        var mathUtility = new MathUtility(new Random(1));
        var ecsContext = BuildEcsContext(world, mathUtility, out var context);
        var resolver = context.ProcessingTierResolver;
        world.EntityPlaced += resolver.EnsureTiered; // As GameBootstrapper wires it.

        var playerEntityId = FloorBuilder.ReservePlayerEntity(ecsContext);
        var origin = FloorBuilder.PlayerSpawnOrigin();
        resolver.SetReferencePosition(origin);

        var raisedDuringPopulation = new HashSet<int>();
        context.ProcessingTierEvents.TierChanged += (entityId, _) => raisedDuringPopulation.Add(entityId);

        var factory = FactoryFor(world, ecsContext, context, mathUtility, resolver);
        FloorBuilder.PopulateFloor(world, ecsContext, new NeighborhoodRecords(mathUtility), factory, context.Terrain, context.Definitions);
        FloorBuilder.CreatePlayer(world, ecsContext, mathUtility, factory, context.Definitions, playerEntityId, resolver);
        world.PlayerEntityId = playerEntityId;

        var transforms = ecsContext.ComponentManager.GetDirectPool<TransformComponent>();
        var tiers = ecsContext.ComponentManager.GetDirectPool<Game.Modules.ProcessingTier.Components.ProcessingTierComponent>();
        var sawNeighborhood = false;
        var sawStationaryLocal = false;
        var movers = ecsContext.ComponentManager.GetPackedPool<MovementComponent>();

        for (var entityId = 0; entityId < transforms.Capacity; entityId++)
        {
            if (!transforms.Has(entityId) || !IsIndexedAt(world, entityId, transforms.GetReadonly(entityId).Position))
            {
                continue;
            }

            Assert.IsTrue(tiers.Has(entityId), $"Entity {entityId} is on the map but was never tiered.");
            var actual = tiers.GetReadonly(entityId).Tier;

            if (entityId == playerEntityId)
            {
                Assert.AreEqual(Game.Modules.ProcessingTier.Components.ProcessingTierLevel.Local, actual);
                continue;
            }

            var expected = ProcessingTierResolver.ComputeTier(transforms.GetReadonly(entityId).Position, origin, previousTier: null);
            Assert.AreEqual(expected, actual, $"Entity {entityId} at {transforms.GetReadonly(entityId).Position}.");

            sawNeighborhood |= expected == Game.Modules.ProcessingTier.Components.ProcessingTierLevel.Neighborhood;
            sawStationaryLocal |= expected == Game.Modules.ProcessingTier.Components.ProcessingTierLevel.Local && !movers.Has(entityId);
        }

        Assert.IsTrue(sawNeighborhood, "Precondition: the map should extend past the Local radius.");
        Assert.IsTrue(sawStationaryLocal, "Precondition: a stationary entity (a wall or shop) near the spawn -- the case that used to be permanently Beyond.");

        // Terrain is cells, not entities: every floored cell holds some, and none of it needed a tier.
        for (var x = world.Map.Bounds.MinX; x < world.Map.Bounds.MaxX; x++)
        {
            for (var y = world.Map.Bounds.MinY; y < world.Map.Bounds.MaxY; y++)
            {
                Assert.IsFalse(world.Map.GetTerrain(x, y, TerrainLayer.Ground).IsEmpty, $"Ground at ({x},{y}) has no terrain.");
                Assert.IsFalse(world.Map.GetTerrain(x, y, TerrainLayer.UnderGround).IsEmpty, $"UnderGround at ({x},{y}) has no terrain.");
            }
        }

        // Every mover born Local reached the roster without any TierChanged -- see
        // LocalTierRoster.OnEntityAdded.
        foreach (var moverId in movers.EntityIds)
        {
            if (tiers.TryGetReadonly(moverId, out var tier) && tier.Tier == Game.Modules.ProcessingTier.Components.ProcessingTierLevel.Local)
            {
                Assert.IsTrue(context.LocalTierRoster.IsLocal(moverId), $"Mover {moverId} is Local but missing from LocalTierRoster.");
            }
        }
    }

    /// <summary>Whether the map's own index holds entityId at position -- as the Blocking occupant or a non-Blocking occupant.</summary>
    private static bool IsIndexedAt(Game.World.World world, int entityId, Vector3Int position) =>
        world.IsOnMap(position) &&
        (world.GetEntityIdAt(position) == entityId ||
         world.GetOccupantEntityIdsAt(position).Contains(entityId));

    /// <summary>A map reaching one neighborhood west of the starting one: a 30-row slice of neighborhood -1 beside a 40x30 corner of neighborhood 0.</summary>
    private static readonly MapBounds TwoNeighborhoodSlice = new(-1024, 0, 40, 30, 3);

    private static (Game.World.World World, EcsContext Ecs, Game.TestMapBuilder Builder, BlueprintRegistry Creatures, EntityFactory Factory) BuildForGeneration(MapBounds bounds)
    {
        var world = new Game.World.World(new Map(bounds));
        var mathUtility = new MathUtility(new Random(1));
        var ecsContext = BuildEcsContext(world, mathUtility, out var context);
        var factory = FactoryFor(world, ecsContext, context, mathUtility);
        var builder = new Game.TestMapBuilder(ecsContext.EntityManager, factory, context.Terrain, context.Definitions);
        return (world, ecsContext, builder, context.Definitions, factory);
    }

    private static void Generate(Game.TestMapBuilder builder, Game.World.World world, NeighborhoodRecord record)
    {
        foreach (var _ in builder.GenerateNeighborhood(world, record))
        {
        }
    }

    private static List<(TerrainCell Ground, TerrainCell UnderGround, TerrainCell Wall)> LayoutOf(Game.World.World world, int minX, int maxX, int minY, int maxY)
    {
        var cells = new List<(TerrainCell, TerrainCell, TerrainCell)>();
        for (var y = minY; y < maxY; y++)
        {
            for (var x = minX; x < maxX; x++)
            {
                cells.Add((world.Map.GetTerrain(x, y, TerrainLayer.Ground), world.Map.GetTerrain(x, y, TerrainLayer.UnderGround), world.GetStructureAt(new Vector3Int(x, y, (int)MapLayer.Ground))));
            }
        }

        return cells;
    }

    private static List<bool> OccupancyOf(Game.World.World world, int minX, int maxX, int minY, int maxY)
    {
        var occupied = new List<bool>();
        for (var z = 0; z < 3; z++)
        {
            for (var y = minY; y < maxY; y++)
            {
                for (var x = minX; x < maxX; x++)
                {
                    occupied.Add(world.GetEntityIdAt(new Vector3Int(x, y, z)) != -1);
                }
            }
        }

        return occupied;
    }

    /// <summary>
    /// Neighborhood -1 comes out the same whether it is generated alone or after neighborhood 0: its
    /// layout everywhere, and its population away from the shared edge, where a creature of the
    /// neighborhood generated first can take a cell a multi-tile creature would otherwise straddle.
    /// </summary>
    [TestMethod]
    public void GenerateNeighborhood_DependsOnlyOnItsRecord_NotOnWhatWasGeneratedFirst()
    {
        var alone = BuildForGeneration(TwoNeighborhoodSlice);
        Generate(alone.Builder, alone.World, new NeighborhoodRecord(-1, 0, seed: 42));

        var afterAnother = BuildForGeneration(TwoNeighborhoodSlice);
        Generate(afterAnother.Builder, afterAnother.World, new NeighborhoodRecord(0, 0, seed: 7));
        Generate(afterAnother.Builder, afterAnother.World, new NeighborhoodRecord(-1, 0, seed: 42));

        CollectionAssert.AreEqual(LayoutOf(alone.World, -1024, 0, 0, 30), LayoutOf(afterAnother.World, -1024, 0, 0, 30));
        CollectionAssert.AreEqual(OccupancyOf(alone.World, -1024, -3, 0, 30), OccupancyOf(afterAnother.World, -1024, -3, 0, 30));
    }

    [TestMethod]
    public void GenerateNeighborhood_SameRecordAgain_RepeatsTheLayoutWithAFreshPopulation()
    {
        var record = new NeighborhoodRecord(-1, 0, seed: 42);
        var firstVisit = BuildForGeneration(TwoNeighborhoodSlice);
        Generate(firstVisit.Builder, firstVisit.World, record);
        var secondVisit = BuildForGeneration(TwoNeighborhoodSlice);
        Generate(secondVisit.Builder, secondVisit.World, record);

        Assert.AreEqual(2, record.PopulationCount);
        CollectionAssert.AreEqual(LayoutOf(firstVisit.World, -1024, 0, 0, 30), LayoutOf(secondVisit.World, -1024, 0, 0, 30));
        CollectionAssert.AreNotEqual(OccupancyOf(firstVisit.World, -1024, 0, 0, 30), OccupancyOf(secondVisit.World, -1024, 0, 0, 30));
    }

    [TestMethod]
    public void PopulateFloor_HallwayCrossAndShopsExistOnlyInTheStartingNeighborhood()
    {
        var (world, ecsContext, _, creatures, factory) = BuildForGeneration(TwoNeighborhoodSlice);
        var mathUtility = new MathUtility(new Random(1));
        var records = new NeighborhoodRecords(mathUtility);

        FloorBuilder.PopulateFloor(world, ecsContext, records, factory, world.Terrain, creatures);

        Assert.AreEqual(2, records.Count);
        Assert.IsTrue(records.TryGet(-1, 0, out _));
        Assert.IsFalse(world.GetStructureAt(new Vector3Int(10, 2, (int)MapLayer.Ground)).IsEmpty);
        Assert.IsTrue(LayoutOf(world, -1024, 0, 0, 30).All(static cell => cell.Wall.IsEmpty), "The hallway cross belongs to the starting neighborhood only.");

        var shops = ecsContext.ComponentManager.GetPackedPool<Game.Modules.Shops.Components.ShopComponent>();
        var transforms = ecsContext.ComponentManager.GetDirectPool<TransformComponent>();
        Assert.AreEqual(2, shops.Count);
        for (var denseIndex = 0; denseIndex < shops.Count; denseIndex++)
        {
            Assert.AreEqual(0, Neighborhoods.CellOf(transforms.GetReadonly(shops.GetEntityIdByDenseIndex(denseIndex)).Position.X));
        }
    }

    /// <summary>One yield per layout row, one per population row with the entities it created, and one for the starting neighborhood's fixtures -- together accounting for every entity generation created.</summary>
    [TestMethod]
    public void GenerateNeighborhood_YieldsARowAtATime()
    {
        var (world, ecsContext, builder, _, _) = BuildForGeneration(new MapBounds(0, 0, 40, 30, 3));
        var livingBefore = ecsContext.EntityManager.LivingEntityCount;

        var yields = builder.GenerateNeighborhood(world, new NeighborhoodRecord(0, 0, seed: 42)).ToList();

        Assert.HasCount(30 + 30 + 1, yields);
        Assert.IsTrue(yields.Take(30).All(static created => created == 0));
        Assert.AreEqual(ecsContext.EntityManager.LivingEntityCount - livingBefore, yields.Sum());
    }
}
