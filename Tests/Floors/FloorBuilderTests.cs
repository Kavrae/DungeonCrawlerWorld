using Engine.Bootstrap;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Floors;
using Game.Modules;
using Game.Modules.Class;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Energy;
using Game.Modules.Health;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.Modules.Race;
using Game.World;

namespace Tests.Floors;

[TestClass]
public sealed class FloorBuilderTests
{
    private static EcsContext BuildEcsContext(Game.World.World world, MathUtility mathUtility)
    {
        var movementModule = new MovementModule();
        movementModule.Configure(new GameModuleContext(world, mathUtility, new EventBus()));

        IReadOnlyList<IModule> modules =
        [
            new CoreModule(),
            new EnergyModule(),
            new HealthModule(),
            movementModule,
            new RaceModule(),
            new ClassModule(),
        ];

        return Bootstrapper.Build(modules, initialEntityCapacity: 5000, initialComponentCapacity: 5000);
    }

    /// <summary>
    /// The player must not be placed before/during TestMapBuilder.Populate (PlaceEntityOnMap
    /// has no free-space check, so an earlier player placement could be silently overwritten
    /// by a later wall/creature at the same cell) -- this confirms the player actually lands
    /// on a real, unoccupied, on-map cell once PopulateFloor finishes, and that World.PlayerEntityId
    /// is wired to whatever id the player actually got (not any particular hardcoded value).
    /// </summary>
    [TestMethod]
    public void PopulateFloor_PlacesPlayerOnAFreeOnMapCellAndWiresPlayerEntityId()
    {
        var world = new Game.World.World(new Map(new Vector3Int(20, 20, 3)));
        var mathUtility = new MathUtility(new Random(1));
        var ecsContext = BuildEcsContext(world, mathUtility);

        FloorBuilder.PopulateFloor(world, ecsContext, mathUtility);

        Assert.IsTrue(ecsContext.EntityManager.IsAlive(world.PlayerEntityId));

        var transform = ecsContext.ComponentManager.GetDirectPool<TransformComponent>().GetReadonly(world.PlayerEntityId);
        Assert.IsTrue(world.IsOnMap(transform.Position));
        Assert.AreEqual(world.PlayerEntityId, world.GetEntityIdAt(transform.Position));

        var movement = ecsContext.ComponentManager.GetPackedPool<MovementComponent>().GetReadonly(world.PlayerEntityId);
        Assert.AreEqual(MovementMode.PlayerControlled, movement.MovementMode);
    }
}
