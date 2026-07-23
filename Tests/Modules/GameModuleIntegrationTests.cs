using Engine.Bootstrap;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Energy;
using Game.Modules.Energy.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.World;

namespace Tests.Modules;

/// <summary>
/// Validates the four real Phase 3 modules (not the toy modules in
/// Tests.Bootstrap.BootstrapperTests) register and schedule together correctly through the
/// real Bootstrapper, including MovementModule's declared dependency on Core and Energy.
/// </summary>
[TestClass]
public sealed class GameModuleIntegrationTests
{
    private static MovementModule CreateConfiguredMovementModule(Game.World.World world, MathUtility mathUtility)
    {
        var movementModule = new MovementModule();
        movementModule.Configure(new GameModuleContext(world, mathUtility, new EventBus()));
        return movementModule;
    }

    [TestMethod]
    public void Build_AllFourModules_RegistersEveryComponentType()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var mathUtility = new MathUtility();

        IReadOnlyList<IModule> modules =
        [
            new CoreModule(),
            new EnergyModule(),
            new HealthModule(),
            CreateConfiguredMovementModule(world, mathUtility),
        ];

        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 100, initialComponentCapacity: 50);

        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<TransformComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<DisplayTextComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<GlyphComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<BackgroundComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<EnergyComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<HealthComponent>());
        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<MovementComponent>());
    }

    [TestMethod]
    public void Build_ModulesInReverseDependencyOrder_StillSucceeds()
    {
        // Bootstrapper must topologically sort by declared Dependencies, not trust
        // caller-supplied order -- pass Movement (which depends on Core and Energy) first.
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var mathUtility = new MathUtility();

        IReadOnlyList<IModule> modules =
        [
            CreateConfiguredMovementModule(world, mathUtility),
            new HealthModule(),
            new EnergyModule(),
            new CoreModule(),
        ];

        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 100, initialComponentCapacity: 50);

        Assert.IsTrue(ecsContext.ComponentManager.IsRegistered<MovementComponent>());
    }

    [TestMethod]
    public void Build_ThenCreateEntityAndTick_RunsWithoutThrowing()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var mathUtility = new MathUtility();

        IReadOnlyList<IModule> modules =
        [
            new CoreModule(),
            new EnergyModule(),
            new HealthModule(),
            CreateConfiguredMovementModule(world, mathUtility),
        ];

        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 100, initialComponentCapacity: 50);

        var entityId = ecsContext.EntityManager.CreateEntity();
        var transform = new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1));
        ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Add(entityId, transform);
        world.PlaceEntityOnMap(entityId, transform.Position, ref transform);
        ecsContext.ComponentManager.GetPackedPool<EnergyComponent>().Add(entityId, new EnergyComponent(100, 5, 100));
        ecsContext.ComponentManager.GetPackedPool<HealthComponent>().Add(entityId, new HealthComponent(100, 10, 100));
        ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Add(entityId, new MovementComponent(MovementMode.Random, 10, null, null));

        for (var frame = 0; frame < 30; frame++)
        {
            ecsContext.Update(default);
        }
    }
}