using Engine.Events;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.World;

namespace Tests.World;

/// <summary>
/// Confirms EntityMoved reaches World.Map's node index through WorldEventSync's subscription
/// alone -- the same effect a direct World.MoveEntity call used to have, now reachable only
/// via the event MovementSystem actually publishes.
/// </summary>
[TestClass]
public sealed class WorldEventSyncTests
{
    [TestMethod]
    public void PublishedEntityMoved_UpdatesMapNodeIndexAtOldAndNewPositions()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var eventBus = new EventBus();
        _ = new WorldEventSync(world, eventBus);

        var oldPosition = new Vector3Int(1, 1, 0);
        var newPosition = new Vector3Int(2, 1, 0);
        var transform = new TransformComponent(oldPosition, new Vector3Byte(1, 1, 1));
        world.PlaceEntityOnMap(entityId: 7, oldPosition, ref transform);

        eventBus.Publish(new EntityMoved(7, oldPosition, newPosition, new Vector3Byte(1, 1, 1)));

        Assert.AreEqual(-1, world.Map.GetMapNode(oldPosition).EntityId);
        Assert.AreEqual(7, world.Map.GetMapNode(newPosition).EntityId);
    }

    [TestMethod]
    public void PublishedEntityMoved_MultiTileEntity_UpdatesEveryOccupiedCell()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        var eventBus = new EventBus();
        _ = new WorldEventSync(world, eventBus);

        var oldPosition = new Vector3Int(0, 0, 0);
        var newPosition = new Vector3Int(1, 0, 0);
        var size = new Vector3Byte(2, 1, 1);
        var transform = new TransformComponent(oldPosition, size);
        world.PlaceEntityOnMap(entityId: 3, oldPosition, ref transform);

        eventBus.Publish(new EntityMoved(3, oldPosition, newPosition, size));

        Assert.AreEqual(-1, world.Map.GetMapNode(new Vector3Int(0, 0, 0)).EntityId);
        Assert.AreEqual(3, world.Map.GetMapNode(new Vector3Int(1, 0, 0)).EntityId);
        Assert.AreEqual(3, world.Map.GetMapNode(new Vector3Int(2, 0, 0)).EntityId);
    }
}
