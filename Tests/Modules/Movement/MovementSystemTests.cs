using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Energy.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Movement.Systems;
using Game.World;

namespace Tests.Modules.Movement;

[TestClass]
public sealed class MovementSystemTests
{
    /// <summary>
    /// A minimal IMapQuery test double with no Game.World.World involved at all -- proves
    /// MovementSystem's dependency on World was actually removed, not just hidden behind an
    /// interface that only World happens to implement.
    /// </summary>
    private sealed class FakeMapQuery(Vector3Int mapSize) : IMapQuery
    {
        public Vector3Int MapSize { get; } = mapSize;
        public bool IsOnMap(Vector3Int position) =>
            position.X >= 0 && position.Y >= 0 && position.Z >= 0
            && position.X < MapSize.X && position.Y < MapSize.Y && position.Z < MapSize.Z;
        public int GetEntityIdAt(Vector3Int position) => -1;
        public bool IsBlocking(int entityId) => true;
    }

    private static DirectComponentPool<TransformComponent> CreateTransformPool(int capacity = 10) =>
        new(capacity, static (ref TransformComponent existing, TransformComponent incoming) => existing = incoming);

    private static PackedComponentPool<EnergyComponent> CreateEnergyPool(int capacity = 10) =>
        new(capacity, capacity, static (ref EnergyComponent existing, EnergyComponent incoming) => existing = incoming);

    private static PackedComponentPool<MovementComponent> CreateMovementPool(int capacity = 10) =>
        new(capacity, capacity, static (ref MovementComponent existing, MovementComponent incoming) => existing = incoming);

    private static MultiComponentPool<NonBlockingComponent> CreateNonBlockingPool(int capacity = 10) =>
        new(capacity, capacity);

    [TestMethod]
    public void Update_MissingEnergyOrTransformComponent_IsSkippedWithoutThrowing()
    {
        var transformPool = CreateTransformPool();
        var energyPool = CreateEnergyPool();
        var movementPool = CreateMovementPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, null));
        // Entity 0 has no TransformComponent or EnergyComponent registered.

        var system = new MovementSystem(transformPool, energyPool, movementPool, world, new MathUtility(), new EventBus());

        system.Update(default, 0);
    }

    [TestMethod]
    public void Update_FramesToWaitPositive_DecrementsAndDoesNotMove()
    {
        var transformPool = CreateTransformPool();
        var energyPool = CreateEnergyPool();
        var movementPool = CreateMovementPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        var transform = new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1));
        transformPool.Add(0, transform);
        world.PlaceEntityOnMap(0, transform.Position, ref transform);
        energyPool.Add(0, new EnergyComponent(100, 0, 100));
        var movement = new MovementComponent(MovementMode.Random, 10, null, null) { FramesToWait = 3 };
        movementPool.Add(0, movement);

        var system = new MovementSystem(transformPool, energyPool, movementPool, world, new MathUtility(), new EventBus());
        system.Update(default, 0);

        Assert.AreEqual(2, movementPool.GetReadonly(0).FramesToWait);
        Assert.AreEqual(new Vector3Int(2, 2, 0), transformPool.GetReadonly(0).Position);
    }

    [TestMethod]
    public void Update_InsufficientEnergy_DoesNotMove()
    {
        var transformPool = CreateTransformPool();
        var energyPool = CreateEnergyPool();
        var movementPool = CreateMovementPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        var transform = new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1));
        transformPool.Add(0, transform);
        world.PlaceEntityOnMap(0, transform.Position, ref transform);
        energyPool.Add(0, new EnergyComponent(5, 0, 100)); // Less than EnergyToMove.
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 40, null, null));

        var system = new MovementSystem(transformPool, energyPool, movementPool, world, new MathUtility(), new EventBus());
        system.Update(default, 0);

        Assert.AreEqual(new Vector3Int(2, 2, 0), transformPool.GetReadonly(0).Position);
    }

    /// <summary>
    /// Regression test for the CanMove fix (decision #8): Old's multi-tile collision check
    /// only inspected cells that were OFF the map (an inverted condition), so an on-map
    /// cell already occupied by another entity was never actually treated as blocking.
    /// This sets up a 2x1 entity with all four neighboring positions invalid -- two via map
    /// edges, two via other entities occupying on-map cells in the target footprint -- and
    /// asserts it doesn't move. Under the old bug, the two on-map-blocked directions would
    /// have incorrectly been treated as free, and the entity would move into an occupied cell.
    /// </summary>
    [TestMethod]
    public void Update_MultiTileEntitySurroundedByOnMapObstacles_DoesNotMoveIntoOccupiedCell()
    {
        var transformPool = CreateTransformPool();
        var energyPool = CreateEnergyPool();
        var movementPool = CreateMovementPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        // Mover: 2x1 footprint at (0,0,0). North and East are blocked by the map edge
        // (Position.X==0, Position.Y==0). South (target footprint (0,1,0)+(1,1,0)) and
        // West (target footprint (1,0,0)+(2,0,0)) are blocked by other entities occupying
        // one cell of each target footprint -- both clearly on-map.
        var moverTransform = new TransformComponent(new Vector3Int(0, 0, 0), new Vector2Byte(2, 1));
        transformPool.Add(0, moverTransform);
        world.PlaceEntityOnMap(0, moverTransform.Position, ref moverTransform);
        energyPool.Add(0, new EnergyComponent(100, 0, 100));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, null));

        var southBlockerTransform = new TransformComponent(new Vector3Int(), new Vector2Byte(1, 1));
        transformPool.Add(1, southBlockerTransform);
        world.PlaceEntityOnMap(1, new Vector3Int(1, 1, 0), ref southBlockerTransform);

        var westBlockerTransform = new TransformComponent(new Vector3Int(), new Vector2Byte(1, 1));
        transformPool.Add(2, westBlockerTransform);
        world.PlaceEntityOnMap(2, new Vector3Int(2, 0, 0), ref westBlockerTransform);

        var system = new MovementSystem(transformPool, energyPool, movementPool, world, new MathUtility(new Random(1)), new EventBus());
        system.Update(default, 0);

        Assert.AreEqual(new Vector3Int(0, 0, 0), transformPool.GetReadonly(0).Position);
        Assert.IsNull(movementPool.GetReadonly(0).NextMapPosition);
        // All four directions exhausted -- SetRandomMapPosition falls through to the
        // "no valid options" branch, which sets FramesToWait.
        Assert.AreEqual(10, movementPool.GetReadonly(0).FramesToWait);
    }

    /// <summary>
    /// Confirms MovementSystem runs against a bare IMapQuery fake with no World anywhere in
    /// the object graph, and that a confirmed move publishes EntityMoved rather than calling
    /// into World directly -- the two halves of decision #2's read/write split.
    /// </summary>
    [TestMethod]
    public void Update_SuccessfulMove_PublishesEntityMovedWithOldAndNewPosition()
    {
        var transformPool = CreateTransformPool();
        var energyPool = CreateEnergyPool();
        var movementPool = CreateMovementPool();
        var mapQuery = new FakeMapQuery(new Vector3Int(5, 5, 1));
        var eventBus = new EventBus();

        var startPosition = new Vector3Int(2, 2, 0);
        transformPool.Add(0, new TransformComponent(startPosition, new Vector2Byte(1, 1)));
        energyPool.Add(0, new EnergyComponent(100, 0, 100));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, null));

        EntityMoved? received = null;
        eventBus.Subscribe<EntityMoved>(e => received = e);

        var system = new MovementSystem(transformPool, energyPool, movementPool, mapQuery, new MathUtility(new Random(1)), eventBus);
        system.Update(default, 0);

        Assert.IsNotNull(received);
        Assert.AreEqual(0, received.Value.EntityId);
        Assert.AreEqual(startPosition, received.Value.OldPosition);
        Assert.AreEqual(transformPool.GetReadonly(0).Position, received.Value.NewPosition);
        Assert.AreNotEqual(startPosition, received.Value.NewPosition);
    }

    /// <summary>
    /// Mirrors Update_MultiTileEntitySurroundedByOnMapObstacles_DoesNotMoveIntoOccupiedCell's
    /// setup (corner position, two directions excluded by the map edge, the remaining two
    /// occupied by other Blocking entities) but for a non-Blocking mover -- where that test
    /// asserts the entity gets stuck (FramesToWait set, all four directions exhausted), a
    /// non-Blocking mover must bypass the occupancy comparison entirely (see CanMove, which
    /// only ever asks IMapQuery.IsBlocking -- it doesn't know or care whether that's backed by
    /// NonBlockingComponent, ForceBlockingComponent, or anything else) and move regardless of
    /// the two blockers.
    /// </summary>
    [TestMethod]
    public void Update_NonBlockingMover_BypassesEntitiesBlockingEveryOtherDirection()
    {
        var transformPool = CreateTransformPool();
        var energyPool = CreateEnergyPool();
        var movementPool = CreateMovementPool();
        var nonBlockingPool = CreateNonBlockingPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1))) { NonBlockingComponents = nonBlockingPool };

        var moverTransform = new TransformComponent(new Vector3Int(0, 0, 0), new Vector2Byte(1, 1));
        transformPool.Add(0, moverTransform);
        world.PlaceEntityOnMap(0, moverTransform.Position, ref moverTransform);
        energyPool.Add(0, new EnergyComponent(100, 0, 100));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, null));
        nonBlockingPool.Add(0, new NonBlockingComponent());

        var southBlockerTransform = new TransformComponent(new Vector3Int(), new Vector2Byte(1, 1));
        transformPool.Add(1, southBlockerTransform);
        world.PlaceEntityOnMap(1, new Vector3Int(0, 1, 0), ref southBlockerTransform);

        var westBlockerTransform = new TransformComponent(new Vector3Int(), new Vector2Byte(1, 1));
        transformPool.Add(2, westBlockerTransform);
        world.PlaceEntityOnMap(2, new Vector3Int(1, 0, 0), ref westBlockerTransform);

        var system = new MovementSystem(transformPool, energyPool, movementPool, world, new MathUtility(new Random(1)), new EventBus());
        system.Update(default, 0);

        Assert.AreNotEqual(10, movementPool.GetReadonly(0).FramesToWait);
        Assert.AreNotEqual(new Vector3Int(0, 0, 0), transformPool.GetReadonly(0).Position);
    }
}
