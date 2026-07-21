using Engine.Math;
using Game.Modules.Core.Components;
using Game.World;

namespace Tests.World;

[TestClass]
public sealed class MapTests
{
    [TestMethod]
    public void GetEntityId_NewMap_EveryCellStartsEmpty()
    {
        var map = new Map(new Vector3Int(3, 3, 3));

        Assert.AreEqual(-1, map.GetEntityId(new Vector3Int(1, 1, 1)));
    }

    [TestMethod]
    public void SetEntityId_ThenGetEntityId_RoundTrips()
    {
        var map = new Map(new Vector3Int(3, 3, 3));

        map.SetEntityId(new Vector3Int(1, 2, 0), 7);

        Assert.AreEqual(7, map.GetEntityId(new Vector3Int(1, 2, 0)));
    }

    [TestMethod]
    public void ClearIfOccupiedBy_MatchingEntityId_ClearsAndReturnsTrue()
    {
        var map = new Map(new Vector3Int(3, 3, 3));
        map.SetEntityId(new Vector3Int(1, 1, 1), 5);

        var cleared = map.ClearIfOccupiedBy(new Vector3Int(1, 1, 1), 5);

        Assert.IsTrue(cleared);
        Assert.AreEqual(-1, map.GetEntityId(new Vector3Int(1, 1, 1)));
    }

    /// <summary>
    /// The compare-and-clear guard MoveEntity relies on to avoid corrupting a different
    /// entity's occupancy when a caller passes a stale old position (see WorldTests).
    /// </summary>
    [TestMethod]
    public void ClearIfOccupiedBy_DifferentEntityId_DoesNotClearAndReturnsFalse()
    {
        var map = new Map(new Vector3Int(3, 3, 3));
        map.SetEntityId(new Vector3Int(1, 1, 1), 5);

        var cleared = map.ClearIfOccupiedBy(new Vector3Int(1, 1, 1), 6);

        Assert.IsFalse(cleared);
        Assert.AreEqual(5, map.GetEntityId(new Vector3Int(1, 1, 1)));
    }

    [TestMethod]
    public void GetTerrainEntityId_NewMap_EveryCellStartsEmpty()
    {
        var map = new Map(new Vector3Int(3, 3, 3));

        Assert.AreEqual(-1, map.GetTerrainEntityId(1, 1, TerrainLayer.Ground));
    }

    [TestMethod]
    public void SetTerrainEntityId_ThenGetTerrainEntityId_RoundTrips()
    {
        var map = new Map(new Vector3Int(3, 3, 3));

        map.SetTerrainEntityId(1, 1, TerrainLayer.Ground, 4);

        Assert.AreEqual(4, map.GetTerrainEntityId(1, 1, TerrainLayer.Ground));
    }

    /// <summary>
    /// Terrain's whole reason for existing: a StoneFloor entity and a Wall entity at the same
    /// (x,y) used to compete for Map's single creature-occupancy slot, with the second write
    /// silently clobbering the first. Terrain and creature occupancy are now independent
    /// stores, so placing both at the same cell must not disturb either.
    /// </summary>
    [TestMethod]
    public void SetEntityId_AndSetTerrainEntityId_SameCell_DoNotClobberEachOther()
    {
        var map = new Map(new Vector3Int(3, 3, 3));

        map.SetTerrainEntityId(1, 1, TerrainLayer.Ground, 4); // e.g. StoneFloor.
        map.SetEntityId(new Vector3Int(1, 1, (int)MapLayer.Ground), 9); // e.g. Wall.

        Assert.AreEqual(4, map.GetTerrainEntityId(1, 1, TerrainLayer.Ground));
        Assert.AreEqual(9, map.GetEntityId(new Vector3Int(1, 1, (int)MapLayer.Ground)));
    }

    [TestMethod]
    public void GetTerrainEntityId_UnderGroundAndGroundLayers_AreIndependent()
    {
        var map = new Map(new Vector3Int(3, 3, 3));

        map.SetTerrainEntityId(1, 1, TerrainLayer.UnderGround, 1);
        map.SetTerrainEntityId(1, 1, TerrainLayer.Ground, 2);

        Assert.AreEqual(1, map.GetTerrainEntityId(1, 1, TerrainLayer.UnderGround));
        Assert.AreEqual(2, map.GetTerrainEntityId(1, 1, TerrainLayer.Ground));
    }
}
