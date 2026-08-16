using Engine.Bootstrap;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.World;

namespace Tests.Modules;

/// <summary>
/// Validates the four real Phase 3 modules (not the toy modules in
/// Tests.Bootstrap.BootstrapperTests) register and schedule together correctly through the
/// real Bootstrapper, including MovementModule's declared dependency on Core.
/// </summary>
[TestClass]
public sealed class GameModuleIntegrationTests
{
    /// <summary>All IGameModules sharing one Bootstrapper.Build call must Configure off the same GameModuleContext instance, so they share one ProcessingTierEvents object -- separate contexts would leave ActionLockSystem's TierChanged subscription listening to a different event than the one ProcessingTierSystem actually raises on.</summary>
    private static (CoreModule Core, HealthModule Health, MovementModule Movement, ProcessingTierModule ProcessingTier) CreateConfiguredModules(Game.World.World world, MathUtility mathUtility)
    {
        var context = new GameModuleContext(world, mathUtility, new EventBus()) { EntityMoveSync = new WorldEventSync(world) };

        var coreModule = new CoreModule();
        coreModule.Configure(context);

        var healthModule = new HealthModule();
        healthModule.Configure(context);

        var movementModule = new MovementModule();
        movementModule.Configure(context);

        var processingTierModule = new ProcessingTierModule();
        processingTierModule.Configure(context);

        return (coreModule, healthModule, movementModule, processingTierModule);
    }

    [TestMethod]
    public void Build_AllFourModules_RegistersEveryComponentType()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var mathUtility = new MathUtility();
        var (coreModule, healthModule, movementModule, processingTierModule) = CreateConfiguredModules(world, mathUtility);

        IReadOnlyList<IModule> modules =
        [
            coreModule,
            healthModule,
            movementModule,
            processingTierModule,
        ];

        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 100, initialComponentCapacity: 50);

        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<TransformComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<DisplayTextComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<GlyphComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<BackgroundComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<ActionLockComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<HealthComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<MovementComponent>());
    }

    [TestMethod]
    public void Build_ModulesInReverseDependencyOrder_StillSucceeds()
    {
        // Bootstrapper must topologically sort by declared Dependencies, not trust
        // caller-supplied order -- pass Movement (which depends on Core) first.
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var mathUtility = new MathUtility();
        var (coreModule, healthModule, movementModule, processingTierModule) = CreateConfiguredModules(world, mathUtility);

        IReadOnlyList<IModule> modules =
        [
            movementModule,
            healthModule,
            coreModule,
            processingTierModule,
        ];

        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 100, initialComponentCapacity: 50);

        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<MovementComponent>());
    }

    [TestMethod]
    public void Build_ThenCreateEntityAndTick_RunsWithoutThrowing()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var mathUtility = new MathUtility();
        var (coreModule, healthModule, movementModule, processingTierModule) = CreateConfiguredModules(world, mathUtility);

        IReadOnlyList<IModule> modules =
        [
            coreModule,
            healthModule,
            movementModule,
            processingTierModule,
        ];

        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 100, initialComponentCapacity: 50);

        var entityId = ecsContext.EntityManager.CreateEntity();
        var transform = new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1));
        ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Add(entityId, transform);
        world.PlaceEntityOnMap(entityId, transform.Position, ref transform);
        ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Add(entityId, new ActionLockComponent(standardLockFrames: 10, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        ecsContext.ComponentManager.GetPackedPool<HealthComponent>().Add(entityId, new HealthComponent(100, 100));
        ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Add(entityId, new MovementComponent(MovementMode.Random, null, null));

        for (var frame = 0; frame < 30; frame++)
        {
            ecsContext.Update(default);
        }
    }
}