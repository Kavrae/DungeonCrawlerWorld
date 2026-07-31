using Engine.Math;
using Game.Modules.Core.Components;
using Game.World;

namespace Tests.World;

/// <summary>
/// Confirms EntityMoved reaches World.Map's node index through WorldEventSync.SyncMove alone --
/// the same effect a direct World.MoveEntity call used to have. MovementSystem now calls
/// SyncMove directly (see IEntityMoveSync's own doc comment for why this moved off EventBus),
/// so these tests call it directly too rather than publishing through a bus WorldEventSync no
/// longer subscribes to.
/// </summary>
[TestClass]
public sealed class WorldEventSyncTests
{
    [TestMethod]
    public void SyncMove_UpdatesMapNodeIndexAtOldAndNewPositions()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var worldEventSync = new WorldEventSync(world);

        var oldPosition = new Vector3Int(1, 1, 0);
        var newPosition = new Vector3Int(2, 1, 0);
        var transform = new TransformComponent(oldPosition, new Vector2Byte(1, 1));
        world.PlaceEntityOnMap(entityId: 7, oldPosition, ref transform);

        worldEventSync.SyncMove(new EntityMoved(7, oldPosition, newPosition, new Vector2Byte(1, 1)));

        Assert.AreEqual(-1, world.Map.GetEntityId(oldPosition));
        Assert.AreEqual(7, world.Map.GetEntityId(newPosition));
    }

    [TestMethod]
    public void SyncMove_MultiTileEntity_UpdatesEveryOccupiedCell()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var worldEventSync = new WorldEventSync(world);

        var oldPosition = new Vector3Int(0, 0, 0);
        var newPosition = new Vector3Int(1, 0, 0);
        var size = new Vector2Byte(2, 1);
        var transform = new TransformComponent(oldPosition, size);
        world.PlaceEntityOnMap(entityId: 3, oldPosition, ref transform);

        worldEventSync.SyncMove(new EntityMoved(3, oldPosition, newPosition, size));

        Assert.AreEqual(-1, world.Map.GetEntityId(new Vector3Int(0, 0, 0)));
        Assert.AreEqual(3, world.Map.GetEntityId(new Vector3Int(1, 0, 0)));
        Assert.AreEqual(3, world.Map.GetEntityId(new Vector3Int(2, 0, 0)));
    }

    /// <summary>SyncMove reaches World.MoveEntityUnchecked, which must update the non-Blocking index (not Map's Blocking array) for a non-Blocking mover -- the same real path a wandering Ghost/Fairy takes every move.</summary>
    [TestMethod]
    public void SyncMove_NonBlockingEntity_MovesNonBlockingIndexEntryInsteadOfMapNodeIndex()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var nonBlockingPool = new Engine.ECS.Components.Stores.MultiComponentPool<NonBlockingComponent>(10, 10);
        nonBlockingPool.Add(7, new NonBlockingComponent());
        world.NonBlockingComponents = nonBlockingPool;
        var worldEventSync = new WorldEventSync(world);

        var oldPosition = new Vector3Int(1, 1, 0);
        var newPosition = new Vector3Int(2, 1, 0);
        var transform = new TransformComponent(oldPosition, new Vector2Byte(1, 1));
        world.PlaceEntityOnMap(entityId: 7, oldPosition, ref transform);

        worldEventSync.SyncMove(new EntityMoved(7, oldPosition, newPosition, new Vector2Byte(1, 1)));

        Assert.AreEqual(-1, world.Map.GetEntityId(newPosition), "Non-Blocking movers never touch Map's Blocking array.");
        Assert.IsFalse(world.Map.GetNonBlockingEntityIdsAt(oldPosition).Contains(7));
        Assert.IsTrue(world.Map.GetNonBlockingEntityIdsAt(newPosition).Contains(7));
    }
}