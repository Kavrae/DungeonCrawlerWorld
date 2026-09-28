using Engine.ECS.Entities;
using Engine.Bootstrap;
using Engine.Events;
using Engine.Math;
using Game.Bootstrap;
using Game.Modules;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.Modules.Movement.Components;
using Game.World;

namespace Tests.Modules;

/// <summary>
/// Validates the real built-in modules (not the toy modules in Tests.Bootstrap.BootstrapperTests)
/// register and schedule together correctly through the real Bootstrapper.
/// </summary>
[TestClass]
public sealed class GameModuleIntegrationTests
{
    /// <summary>All IGameModules sharing one Bootstrapper.Build call must Configure off the same GameModuleContext instance, so they share one ProcessingTierEvents object -- separate contexts would leave ActionLockSystem's TierChanged subscription listening to a different event than the one ProcessingTierSystem actually raises on.</summary>
    private static GameModuleContext CreateContext(Game.World.World world) =>
        new(world, new MathUtility(), new EventBus()) { PlayerQuery = world, EntityMoveSync = new WorldEventSync(world) };

    [TestMethod]
    public void Build_BuiltInModules_RegistersEveryComponentType()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        var ecsContext = BuiltInTestModules.Build(CreateContext(world));

        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<TransformComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<DisplayTextComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<GlyphComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<BackgroundComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<ActionLockComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<SimpleHealthComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<MovementComponent>());
    }

    [TestMethod]
    public void Build_BuiltInModulesInReverseOrder_StillSucceeds()
    {
        // Requires is about presence, not order: a module listed before the ones it requires still
        // builds, since every component is registered before any system.
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var context = CreateContext(world);
        var modules = GameBootstrapper.BuiltInModules().Reverse().ToList();
        foreach (var gameModule in modules.OfType<IGameModule>())
        {
            gameModule.Configure(context);
        }

        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 100, initialComponentCapacity: 50, context.EventBus, entityKeys: context.EntityKeys);

        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<MovementComponent>());
    }

    [TestMethod]
    public void Build_ThenCreateEntityAndTick_RunsWithoutThrowing()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        var ecsContext = BuiltInTestModules.Build(CreateContext(world));

        var entityId = ecsContext.EntityManager.CreateEntity();
        var transform = new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1));
        ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Add(entityId, transform);
        world.PlaceEntityOnMap(entityId, transform.Position, ref transform);
        ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Add(entityId, new ActionLockComponent(standardLockFrames: 10, currentLockTotalFrames: 0, unlockedAtFrame: 0));
        ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().Add(entityId, new SimpleHealthComponent(100, 100));
        ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Add(entityId, new MovementComponent(MovementMode.Random, null, null));

        for (var frame = 0; frame < 30; frame++)
        {
            ecsContext.Update(default);
        }
    }
}
