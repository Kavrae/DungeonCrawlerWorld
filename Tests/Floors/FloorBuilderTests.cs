using Engine.Bootstrap;
using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
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
using Game.World;

namespace Tests.Floors;

[TestClass]
public sealed class FloorBuilderTests
{
    private static EcsContext BuildEcsContext(Game.World.World world, MathUtility mathUtility) => BuildEcsContext(world, mathUtility, out _);

    private static EcsContext BuildEcsContext(Game.World.World world, MathUtility mathUtility, out GameModuleContext context)
    {
        var eventBus = new EventBus();
        context = new GameModuleContext(world, mathUtility, eventBus) { PlayerQuery = world, EntityMoveSync = new WorldEventSync(world) };

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
        statusEffectsModule.Configure(context);

        var containersModule = new ContainersModule();
        containersModule.Configure(context);

        var shopModule = new ShopModule();
        shopModule.Configure(context);

        var npcBehaviorModule = new NpcBehaviorModule();
        npcBehaviorModule.Configure(context);

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

        return Bootstrapper.Build(modules, initialEntityCapacity: 5000, initialComponentCapacity: 5000);
    }

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
        var ecsContext = BuildEcsContext(world, mathUtility);

        var crawlerNumberAllocator = new UniqueNumberAllocator(mathUtility, 1, 13_000_000);
        var playerEntityId = FloorBuilder.ReservePlayerEntity(ecsContext);
        FloorBuilder.PopulateFloor(world, ecsContext, mathUtility, crawlerNumberAllocator, new FrameEventBuffer<EntityMovedEvent>());
        FloorBuilder.CreatePlayer(world, ecsContext, mathUtility, new FrameEventBuffer<EntityMovedEvent>(), crawlerNumberAllocator, playerEntityId);
        world.PlayerEntityId = playerEntityId;

        Assert.IsTrue(ecsContext.EntityManager.EntityExists(world.PlayerEntityId));

        var transform = ecsContext.ComponentManager.GetDirectPool<TransformComponent>().GetReadonly(world.PlayerEntityId);
        Assert.IsTrue(world.IsOnMap(transform.Position));
        Assert.AreEqual(world.PlayerEntityId, world.GetEntityIdAt(transform.Position));

        var movement = ecsContext.ComponentManager.GetPackedPool<MovementComponent>().GetReadonly(world.PlayerEntityId);
        Assert.AreEqual(MovementMode.PlayerControlled, movement.MovementMode);
    }

    /// <summary>
    /// The actual point of ReservePlayerEntity existing as a separate, earlier call: reserving
    /// before PopulateFloor's ~2.6M NPC/terrain entities exist lands the player on entity id 0
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
    /// indexes -- movers, terrain, walls, shops -- is born with the tier its position deserves, and
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

        var crawlerNumberAllocator = new UniqueNumberAllocator(mathUtility, 1, 13_000_000);
        var playerEntityId = FloorBuilder.ReservePlayerEntity(ecsContext);
        var origin = FloorBuilder.PlayerSpawnOrigin(world);
        resolver.SetReferencePosition(origin);

        var raisedDuringPopulation = new HashSet<int>();
        context.ProcessingTierEvents.TierChanged += (entityId, _) => raisedDuringPopulation.Add(entityId);

        FloorBuilder.PopulateFloor(world, ecsContext, mathUtility, crawlerNumberAllocator, new FrameEventBuffer<EntityMovedEvent>(), resolver);
        FloorBuilder.CreatePlayer(world, ecsContext, mathUtility, new FrameEventBuffer<EntityMovedEvent>(), crawlerNumberAllocator, playerEntityId, resolver);
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
        Assert.IsTrue(sawStationaryLocal, "Precondition: a stationary entity (terrain, wall, shop) near the spawn -- the case that used to be permanently Beyond.");

        // Tier-first is what keeps population cheap: terrain -- ~2M of the ~2.6M entities in the
        // real game -- must be born tiered, never corrected after the fact with an event.
        for (var x = 0; x < world.Map.Size.X; x++)
        {
            for (var y = 0; y < world.Map.Size.Y; y++)
            {
                var groundTerrain = world.Map.GetTerrainEntityId(x, y, TerrainLayer.Ground);
                Assert.IsFalse(groundTerrain >= 0 && raisedDuringPopulation.Contains(groundTerrain), $"Terrain entity {groundTerrain} at ({x},{y}) was tiered by event instead of at creation.");
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

    /// <summary>Whether the map's own index holds entityId at position -- as the Blocking occupant, a non-Blocking occupant, or the terrain.</summary>
    private static bool IsIndexedAt(Game.World.World world, int entityId, Vector3Int position) =>
        world.IsOnMap(position) &&
        (world.GetEntityIdAt(position) == entityId ||
         world.GetOccupantEntityIdsAt(position).Contains(entityId) ||
         world.GetTerrainEntityIdAt(position) == entityId);
}
