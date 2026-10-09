using Engine.ECS.Entities;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Bootstrap;
using Game.Modules;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.Modules.Movement.Components;
using Game.World;

namespace Tests.Modules;

/// <summary>
/// Validates the real built-in modules (not the toy modules in Tests.Bootstrap.BootstrapperTests)
/// register and schedule together correctly through the real EcsBuilder.
/// </summary>
[TestClass]
public sealed class GameModuleIntegrationTests
{
    [TestMethod]
    public void Build_BuiltInModules_RegistersEveryComponentType()
    {
        var ecsContext = BuiltInTestModules.Build(new Map(new Vector3Int(5, 5, 1))).EcsContext;

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
        var modules = GameBootstrapper.BuiltInModules().Reverse().CreateAll();

        var ecsContext = BuiltInTestModules.BuildModules(modules, initialEntityCapacity: 100, initialComponentCapacity: 50).EcsContext;

        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<MovementComponent>());
    }

    [TestMethod]
    public void Build_ThenCreateEntityAndTick_RunsWithoutThrowing()
    {
        var pass = BuiltInTestModules.Build(new Map(new Vector3Int(5, 5, 1)));
        var world = pass.World;
        var ecsContext = pass.EcsContext;

        var entityId = ecsContext.EntityManager.CreateEntity();
        var transform = new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1));
        ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Add(entityId, transform);
        world.PlaceEntityOnMap(entityId, transform.Position, ref transform);
        ecsContext.ComponentManager.GetPackedPool<ActionLockComponent>().Add(entityId, new ActionLockComponent(currentLockTotalFrames: 0, unlockedAtFrame: 0));
        ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>().Add(entityId, new SimpleHealthComponent(100, 100));
        ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Add(entityId, new MovementComponent(MovementMode.Random, null, null));

        for (var frame = 0; frame < 30; frame++)
        {
            ecsContext.Update(default);
        }
    }
}
