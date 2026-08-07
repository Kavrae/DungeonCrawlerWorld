using Engine.Bootstrap;
using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Floors;
using Game.Modules;
using Game.Modules.Abilities;
using Game.Modules.AbilityScores;
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

        var abilitiesModule = new AbilitiesModule();
        abilitiesModule.Configure(context);

        var coreAbilitiesModule = new CoreAbilitiesModule();
        coreAbilitiesModule.Configure(context);

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
            abilitiesModule,
            coreAbilitiesModule,
            new StatusEffectsModule(),
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
    /// on a real, unoccupied, on-map cell once CreatePlayer runs (called separately from
    /// PopulateFloor now -- see FloorBuilder's own doc comment for why: GameLoop triggers it
    /// once on its first live Update() tick instead), and that World.PlayerEntityId is wired
    /// to whatever id the player actually got (not any particular hardcoded value).
    /// </summary>
    [TestMethod]
    public void PopulateFloor_PlacesPlayerOnAFreeOnMapCellAndWiresPlayerEntityId()
    {
        var world = new Game.World.World(new Map(new Vector3Int(20, 20, 3)));
        var mathUtility = new MathUtility(new Random(1));
        var ecsContext = BuildEcsContext(world, mathUtility);

        var crawlerNumberAllocator = new UniqueNumberAllocator(mathUtility, 1, 13_000_000);
        FloorBuilder.PopulateFloor(world, ecsContext, mathUtility, crawlerNumberAllocator);
        world.PlayerEntityId = FloorBuilder.CreatePlayer(world, ecsContext, mathUtility, new FrameEventBuffer<EntityMoved>(), crawlerNumberAllocator);

        Assert.IsTrue(ecsContext.EntityManager.IsAlive(world.PlayerEntityId));

        var transform = ecsContext.ComponentManager.GetDirectPool<TransformComponent>().GetReadonly(world.PlayerEntityId);
        Assert.IsTrue(world.IsOnMap(transform.Position));
        Assert.AreEqual(world.PlayerEntityId, world.GetEntityIdAt(transform.Position));

        var movement = ecsContext.ComponentManager.GetPackedPool<MovementComponent>().GetReadonly(world.PlayerEntityId);
        Assert.AreEqual(MovementMode.PlayerControlled, movement.MovementMode);
    }
}
