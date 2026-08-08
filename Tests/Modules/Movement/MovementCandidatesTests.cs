using Engine.Math;
using Game.Modules.Movement;
using Game.World;

namespace Tests.Modules.Movement;

/// <summary>
/// Unit tests for the position-candidate math shared by MovementSystem (re-validating an
/// already-queued move) and TestCombatBehaviorSystem (deciding a new wander destination). Some
/// coverage here re-homes what used to be exercised indirectly through MovementSystem's own
/// Random-mode decision logic before that logic moved out into TestCombatBehaviorSystem.
/// </summary>
[TestClass]
public sealed class MovementCandidatesTests
{
    private static readonly Vector2Byte SingleTile = new(1, 1);

    private sealed class FakeMapQuery(Vector3Int mapSize) : IMapQuery
    {
        private readonly Dictionary<Vector3Int, int> _occupants = [];

        public Vector3Int MapSize { get; } = mapSize;
        public bool IsOnMap(Vector3Int position) =>
            position.X >= 0 && position.Y >= 0 && position.Z >= 0
            && position.X < MapSize.X && position.Y < MapSize.Y && position.Z < MapSize.Z;
        public int GetEntityIdAt(Vector3Int position) => _occupants.TryGetValue(position, out var id) ? id : -1;
        public bool IsBlocking(int entityId) => true;
        public int GetTerrainEntityIdAt(Vector3Int position) => -1;
        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) => entityIds.Fill(-1);

        public void SetOccupant(Vector3Int position, int entityId) => _occupants[position] = entityId;
    }

    [TestMethod]
    public void CanOccupy_PositionOffMap_ReturnsFalse()
    {
        var mapQuery = new FakeMapQuery(new Vector3Int(5, 5, 1));

        Assert.IsFalse(MovementCandidates.CanOccupy(mapQuery, new Vector3Int(-1, 0, 0), SingleTile, entityId: 0, isBlocking: true));
        Assert.IsFalse(MovementCandidates.CanOccupy(mapQuery, new Vector3Int(5, 0, 0), SingleTile, entityId: 0, isBlocking: true));
    }

    [TestMethod]
    public void CanOccupy_SingleTileBlockingMover_TileOccupiedByAnotherEntity_ReturnsFalse()
    {
        var mapQuery = new FakeMapQuery(new Vector3Int(5, 5, 1));
        mapQuery.SetOccupant(new Vector3Int(2, 2, 0), entityId: 99);

        Assert.IsFalse(MovementCandidates.CanOccupy(mapQuery, new Vector3Int(2, 2, 0), SingleTile, entityId: 0, isBlocking: true));
    }

    [TestMethod]
    public void CanOccupy_SingleTileBlockingMover_TileOccupiedBySelf_ReturnsTrue()
    {
        var mapQuery = new FakeMapQuery(new Vector3Int(5, 5, 1));
        mapQuery.SetOccupant(new Vector3Int(2, 2, 0), entityId: 0);

        Assert.IsTrue(MovementCandidates.CanOccupy(mapQuery, new Vector3Int(2, 2, 0), SingleTile, entityId: 0, isBlocking: true));
    }

    /// <summary>Regression test for the CanMove fix (decision #8): a multi-tile footprint's collision check must inspect every on-map cell of the target footprint, not just cells that happen to be off-map.</summary>
    [TestMethod]
    public void CanOccupy_MultiTileFootprint_OneCellOnMapButOccupied_ReturnsFalse()
    {
        var mapQuery = new FakeMapQuery(new Vector3Int(5, 5, 1));
        mapQuery.SetOccupant(new Vector3Int(1, 1, 0), entityId: 99);

        Assert.IsFalse(MovementCandidates.CanOccupy(mapQuery, new Vector3Int(0, 1, 0), new Vector2Byte(2, 1), entityId: 0, isBlocking: true));
    }

    [TestMethod]
    public void CanOccupy_NotBlockingMover_IgnoresOccupancyButStillRequiresOnMap()
    {
        var mapQuery = new FakeMapQuery(new Vector3Int(5, 5, 1));
        mapQuery.SetOccupant(new Vector3Int(2, 2, 0), entityId: 99);

        Assert.IsTrue(MovementCandidates.CanOccupy(mapQuery, new Vector3Int(2, 2, 0), SingleTile, entityId: 0, isBlocking: false));
        Assert.IsFalse(MovementCandidates.CanOccupy(mapQuery, new Vector3Int(-1, 0, 0), SingleTile, entityId: 0, isBlocking: false));
    }

    [TestMethod]
    public void TryPickRandomAdjacentPosition_AllFourDirectionsBlocked_ReturnsFalse()
    {
        var mapQuery = new FakeMapQuery(new Vector3Int(5, 5, 1));
        // Corner placement excludes North/East via map edges; the remaining two on-map
        // directions (South/West) are occupied by other Blocking entities.
        mapQuery.SetOccupant(new Vector3Int(0, 1, 0), entityId: 1); // South of (0,0)
        mapQuery.SetOccupant(new Vector3Int(1, 0, 0), entityId: 2); // West of (0,0)
        var mathUtility = new MathUtility(new Random(1));

        var found = MovementCandidates.TryPickRandomAdjacentPosition(mapQuery, mathUtility, entityId: 0, new Vector3Int(0, 0, 0), SingleTile, isBlocking: true, out _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void TryPickRandomAdjacentPosition_ExactlyOneDirectionOpen_ReturnsIt()
    {
        var mapQuery = new FakeMapQuery(new Vector3Int(5, 5, 1));
        var origin = new Vector3Int(2, 2, 0);
        // Away from any map edge, so all 4 directions start viable; occupy 3 of them, leaving
        // exactly (3,2,0) (west, per MovementCandidates' direction mapping) free.
        mapQuery.SetOccupant(new Vector3Int(2, 1, 0), entityId: 1); // North
        mapQuery.SetOccupant(new Vector3Int(2, 3, 0), entityId: 2); // South
        mapQuery.SetOccupant(new Vector3Int(1, 2, 0), entityId: 3); // East
        var mathUtility = new MathUtility(new Random(1));

        var found = MovementCandidates.TryPickRandomAdjacentPosition(mapQuery, mathUtility, entityId: 0, origin, SingleTile, isBlocking: true, out var candidate);

        Assert.IsTrue(found);
        Assert.AreEqual(new Vector3Int(3, 2, 0), candidate);
    }

    /// <summary>A non-Blocking mover bypasses occupancy entirely (see CanOccupy), so a direction occupied by another Blocking entity is still a valid pick -- only the map edge actually constrains it.</summary>
    [TestMethod]
    public void TryPickRandomAdjacentPosition_NonBlockingMover_BypassesOtherBlockingEntities()
    {
        var mapQuery = new FakeMapQuery(new Vector3Int(5, 5, 1));
        var origin = new Vector3Int(0, 0, 0);
        mapQuery.SetOccupant(new Vector3Int(0, 1, 0), entityId: 1); // South
        mapQuery.SetOccupant(new Vector3Int(1, 0, 0), entityId: 2); // West
        var mathUtility = new MathUtility(new Random(2));

        var found = MovementCandidates.TryPickRandomAdjacentPosition(mapQuery, mathUtility, entityId: 0, origin, SingleTile, isBlocking: false, out var candidate);

        Assert.IsTrue(found);
        Assert.IsTrue(mapQuery.IsOnMap(candidate));
    }
}
