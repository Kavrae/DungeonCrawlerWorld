using Engine.Math;
using Game.Modules.Core.Components;
using Game.World;

namespace Tests.World;

[TestClass]
public sealed class WorldTests
{
    private static Game.World.World CreateWorld(int sizeX = 10, int sizeY = 10, int sizeZ = 2) =>
        new(new Map(new Vector3Int(sizeX, sizeY, sizeZ)));

    [TestMethod]
    public void IsOnMap_WithinBounds_ReturnsTrue()
    {
        var world = CreateWorld();

        Assert.IsTrue(world.IsOnMap(new Vector3Int(5, 5, 1)));
    }

    [TestMethod]
    public void IsOnMap_OutOfBounds_ReturnsFalse()
    {
        var world = CreateWorld();

        Assert.IsFalse(world.IsOnMap(new Vector3Int(10, 5, 1)));
        Assert.IsFalse(world.IsOnMap(new Vector3Int(-1, 5, 1)));
    }

    [TestMethod]
    public void IsOnMap_Cube_RequiresBothCornersOnMap()
    {
        var world = CreateWorld();

        // IsOnMap(CubeInt) checks Position and the last occupied cell (Position+Size-1), not
        // Position+Size itself (Size is an extent, not an inclusive far corner). A 2x2x1
        // cube at (7,7,0) occupies up to (8,8,0) -- within the 10x10x2 map.
        Assert.IsTrue(world.IsOnMap(new CubeInt(new Vector3Int(7, 7, 0), new Vector3Int(2, 2, 1))));
        // A 2x2x1 cube at (9,9,0) occupies up to (10,10,0) -- X=10 and Y=10 are off the 10-wide map.
        Assert.IsFalse(world.IsOnMap(new CubeInt(new Vector3Int(9, 9, 0), new Vector3Int(2, 2, 1))));
        // A 1x1x1 cube flush against the map's last valid cell must still be considered on-map.
        Assert.IsTrue(world.IsOnMap(new CubeInt(new Vector3Int(9, 9, 1), new Vector3Int(1, 1, 1))));
    }

    [TestMethod]
    public void PlaceEntityOnMap_SingleTile_SetsMapNodeAndPosition()
    {
        var world = CreateWorld();
        var transform = new TransformComponent(new Vector3Int(), new Vector3Byte(1, 1, 1));

        world.PlaceEntityOnMap(42, new Vector3Int(3, 4, 1), ref transform);

        Assert.AreEqual(42, world.Map.GetMapNode(new Vector3Int(3, 4, 1)).EntityId);
        Assert.AreEqual(new Vector3Int(3, 4, 1), transform.Position);
    }

    [TestMethod]
    public void PlaceEntityOnMap_MultiTile_SetsEveryOccupiedCell()
    {
        var world = CreateWorld();
        var transform = new TransformComponent(new Vector3Int(), new Vector3Byte(2, 2, 1));

        world.PlaceEntityOnMap(7, new Vector3Int(2, 2, 1), ref transform);

        Assert.AreEqual(7, world.Map.GetMapNode(new Vector3Int(2, 2, 1)).EntityId);
        Assert.AreEqual(7, world.Map.GetMapNode(new Vector3Int(3, 2, 1)).EntityId);
        Assert.AreEqual(7, world.Map.GetMapNode(new Vector3Int(2, 3, 1)).EntityId);
        Assert.AreEqual(7, world.Map.GetMapNode(new Vector3Int(3, 3, 1)).EntityId);
    }

    [TestMethod]
    public void PlaceEntityOnMap_OffMap_DoesNothing()
    {
        var world = CreateWorld();
        var transform = new TransformComponent(new Vector3Int(1, 1, 1), new Vector3Byte(1, 1, 1));

        world.PlaceEntityOnMap(1, new Vector3Int(50, 50, 1), ref transform);

        Assert.AreEqual(new Vector3Int(1, 1, 1), transform.Position);
    }

    /// <summary>
    /// Regression test: PlaceEntityOnMap used to bounds-check only the placement's origin
    /// point (IsOnMap(Vector3Int)), not the whole footprint (IsOnMap(CubeInt)) -- harmless
    /// while every entity was 1x1x1 (origin-only and full-footprint checks are equivalent
    /// there), but a multi-tile entity whose origin is on-map with a footprint extending off
    /// it would throw IndexOutOfRangeException writing Map.MapNodes past the array bounds.
    /// A 3x3 entity placed at (8,8,0) on a 10x10 map has an on-map origin but a footprint
    /// reaching (10,10,0), which is off the map.
    /// </summary>
    [TestMethod]
    public void PlaceEntityOnMap_OriginOnMapButFootprintOffMap_DoesNothingAndDoesNotThrow()
    {
        var world = CreateWorld();
        var transform = new TransformComponent(new Vector3Int(1, 1, 1), new Vector3Byte(3, 3, 1));

        world.PlaceEntityOnMap(1, new Vector3Int(8, 8, 0), ref transform);

        Assert.AreEqual(new Vector3Int(1, 1, 1), transform.Position);
        Assert.AreEqual(-1, world.Map.GetMapNode(new Vector3Int(8, 8, 0)).EntityId);
    }

    [TestMethod]
    public void MoveEntity_SingleTile_ClearsOldCellAndSetsNewCell()
    {
        var world = CreateWorld();
        var transform = new TransformComponent(new Vector3Int(2, 2, 1), new Vector3Byte(1, 1, 1));
        world.PlaceEntityOnMap(5, transform.Position, ref transform);

        world.MoveEntity(5, new Vector3Int(3, 2, 1), transform);

        Assert.AreEqual(-1, world.Map.GetMapNode(new Vector3Int(2, 2, 1)).EntityId);
        Assert.AreEqual(5, world.Map.GetMapNode(new Vector3Int(3, 2, 1)).EntityId);
    }

    [TestMethod]
    public void MoveEntity_MultiTile_ClearsOldFootprintAndSetsNewFootprint()
    {
        var world = CreateWorld();
        var transform = new TransformComponent(new Vector3Int(2, 2, 1), new Vector3Byte(2, 2, 1));
        world.PlaceEntityOnMap(5, transform.Position, ref transform);

        world.MoveEntity(5, new Vector3Int(3, 2, 1), transform);

        Assert.AreEqual(-1, world.Map.GetMapNode(new Vector3Int(2, 2, 1)).EntityId);
        Assert.AreEqual(-1, world.Map.GetMapNode(new Vector3Int(2, 3, 1)).EntityId);
        Assert.AreEqual(5, world.Map.GetMapNode(new Vector3Int(3, 2, 1)).EntityId);
        Assert.AreEqual(5, world.Map.GetMapNode(new Vector3Int(4, 2, 1)).EntityId);
        Assert.AreEqual(5, world.Map.GetMapNode(new Vector3Int(3, 3, 1)).EntityId);
        Assert.AreEqual(5, world.Map.GetMapNode(new Vector3Int(4, 3, 1)).EntityId);
    }

    /// <summary>
    /// Regression test: MoveEntity used to write straight into Map.MapNodes with no bounds
    /// check at all ("assumes valid starting and ending positions"), so an out-of-bounds
    /// newPosition threw IndexOutOfRangeException with nothing upstream to catch it. Now it
    /// no-ops like PlaceEntityOnMap already does for an invalid footprint, and the entity's
    /// old cell is left untouched since the move didn't happen.
    /// </summary>
    [TestMethod]
    public void MoveEntity_NewPositionOffMap_DoesNothingAndDoesNotThrow()
    {
        var world = CreateWorld();
        var transform = new TransformComponent(new Vector3Int(2, 2, 1), new Vector3Byte(1, 1, 1));
        world.PlaceEntityOnMap(5, transform.Position, ref transform);

        world.MoveEntity(5, new Vector3Int(50, 50, 1), transform);

        Assert.AreEqual(5, world.Map.GetMapNode(new Vector3Int(2, 2, 1)).EntityId);
    }

    /// <summary>
    /// Regression test: MoveEntity used to unconditionally overwrite the destination cell(s)
    /// regardless of what was already there, trusting MovementSystem's own CanMove re-check
    /// to have prevented this. Called directly (bypassing that gate, as a mod's own code
    /// could), it must not silently steal another entity's map presence.
    /// </summary>
    [TestMethod]
    public void MoveEntity_NewPositionOccupiedByDifferentEntity_DoesNothingAndDoesNotStealTheCell()
    {
        var world = CreateWorld();
        var mover = new TransformComponent(new Vector3Int(2, 2, 1), new Vector3Byte(1, 1, 1));
        world.PlaceEntityOnMap(5, mover.Position, ref mover);
        var occupant = new TransformComponent(new Vector3Int(), new Vector3Byte(1, 1, 1));
        world.PlaceEntityOnMap(6, new Vector3Int(3, 2, 1), ref occupant);

        world.MoveEntity(5, new Vector3Int(3, 2, 1), mover);

        Assert.AreEqual(5, world.Map.GetMapNode(new Vector3Int(2, 2, 1)).EntityId);
        Assert.AreEqual(6, world.Map.GetMapNode(new Vector3Int(3, 2, 1)).EntityId);
    }

    /// <summary>
    /// Regression test: the old-footprint clear used to blindly set every cell in the old
    /// footprint to empty, regardless of whether it still recorded this entityId. A caller
    /// passing a stale/wrong old position (transformComponent.Position not actually matching
    /// what's on the map) must not corrupt a different entity's occupancy record.
    /// </summary>
    [TestMethod]
    public void MoveEntity_StaleOldPositionBelongsToDifferentEntity_DoesNotClearTheirCell()
    {
        var world = CreateWorld();
        var other = new TransformComponent(new Vector3Int(), new Vector3Byte(1, 1, 1));
        world.PlaceEntityOnMap(6, new Vector3Int(2, 2, 1), ref other);
        // Entity 5 claims a stale old position that actually belongs to entity 6.
        var staleTransform = new TransformComponent(new Vector3Int(2, 2, 1), new Vector3Byte(1, 1, 1));

        world.MoveEntity(5, new Vector3Int(3, 2, 1), staleTransform);

        Assert.AreEqual(6, world.Map.GetMapNode(new Vector3Int(2, 2, 1)).EntityId);
        Assert.AreEqual(5, world.Map.GetMapNode(new Vector3Int(3, 2, 1)).EntityId);
    }

    [TestMethod]
    public void RemoveEntityFromMap_ClearsOccupiedCells()
    {
        var world = CreateWorld();
        var transform = new TransformComponent(new Vector3Int(), new Vector3Byte(1, 1, 1));
        world.PlaceEntityOnMap(9, new Vector3Int(4, 4, 1), ref transform);

        world.RemoveEntityFromMap(9, ref transform);

        Assert.AreEqual(-1, world.Map.GetMapNode(new Vector3Int(4, 4, 1)).EntityId);
    }
}
