using Engine.Bootstrap;
using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Floors;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Definitions;
using Game.Modules.Burning;
using Game.Modules.Class;
using Game.Modules.ContactDamage;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Crawler;
using Game.Modules.Health;
using Game.Modules.Inventory;
using Game.Modules.Mana;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.Modules.Poison;
using Game.Modules.ProcessingTier;
using Game.Modules.Race;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffectAura;
using Game.Modules.StatusEffects;
using Game.World;

namespace Tests.Floors;

[TestClass]
public sealed class FloorBuilderTests
{
    private static EcsContext BuildEcsContext(Game.World.World world, MathUtility mathUtility)
    {
        var eventBus = new EventBus();
        var context = new GameModuleContext(world, mathUtility, eventBus) { PlayerQuery = world, EntityMoveSync = new WorldEventSync(world) };

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
}
