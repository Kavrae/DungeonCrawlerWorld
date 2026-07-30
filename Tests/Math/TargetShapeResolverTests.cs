using Engine.Math;

namespace Tests.Math;

[TestClass]
public sealed class TargetShapeResolverTests
{
    private static readonly Vector3Int MapSize = new(1000, 1000, 1);

    [TestMethod]
    public void Adjacent_IncludesOriginTileAndAllEightSurroundingNeighbors()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Adjacent, origin, cursorTile: origin, range: 0, areaSize: 0, MapSize, tiles);

        Assert.HasCount(9, tiles);
        CollectionAssert.Contains(tiles, origin);
        CollectionAssert.Contains(tiles, new Vector3Int(9, 10, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(11, 10, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(10, 9, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(10, 11, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(9, 9, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(11, 9, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(9, 11, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(11, 11, 0));
    }

    [TestMethod]
    public void Adjacent_IgnoresRangeAndAreaSize_AlwaysFixedRadiusOne()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Adjacent, origin, cursorTile: origin, range: 99, areaSize: 99, MapSize, tiles);

        Assert.HasCount(9, tiles);
    }

    [TestMethod]
    public void Resolve_ReusedResultsBuffer_IsClearedEachCall()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int> { new(0, 0, 0), new(1, 1, 0) };

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, origin, cursorTile: new Vector3Int(50, 50, 0), range: 1, areaSize: 0, MapSize, tiles);

        Assert.IsEmpty(tiles, "Stale entries from a previous call must not survive into a call that resolves to nothing.");
    }

    [TestMethod]
    public void SingleTarget_WithinRange_ReturnsExactlyTheCursorTile()
    {
        var origin = new Vector3Int(0, 0, 0);
        var cursorTile = new Vector3Int(10, 0, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, origin, cursorTile, range: 10, areaSize: 0, MapSize, tiles);

        Assert.HasCount(1, tiles);
        Assert.AreEqual(cursorTile, tiles[0]);
    }

    [TestMethod]
    public void SingleTarget_BeyondRange_ReturnsNoTiles()
    {
        var origin = new Vector3Int(0, 0, 0);
        var cursorTile = new Vector3Int(11, 0, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, origin, cursorTile, range: 10, areaSize: 0, MapSize, tiles);

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void Burst_CursorBeyondCastRange_ReturnsNoTiles()
    {
        var origin = new Vector3Int(0, 0, 0);
        var cursorTile = new Vector3Int(11, 0, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Burst, origin, cursorTile, range: 10, areaSize: 2, MapSize, tiles);

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void Burst_WithinCastRange_ResolvesDiamondFootprintCenteredOnCursor_NotOnCaster()
    {
        var origin = new Vector3Int(0, 10, 0);
        var cursorTile = new Vector3Int(5, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Burst, origin, cursorTile, range: 10, areaSize: 1, MapSize, tiles);

        // areaSize 1 -> radius 1 diamond around the cursor tile: itself plus 4 cardinal neighbors.
        Assert.HasCount(5, tiles);
        CollectionAssert.Contains(tiles, cursorTile);
        CollectionAssert.DoesNotContain(tiles, origin);
    }

    [TestMethod]
    public void Burst_ZeroAreaSize_DegeneratesToJustTheCursorTile()
    {
        var origin = new Vector3Int(0, 0, 0);
        var cursorTile = new Vector3Int(5, 0, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Burst, origin, cursorTile, range: 10, areaSize: 0, MapSize, tiles);

        Assert.HasCount(1, tiles);
        Assert.AreEqual(cursorTile, tiles[0]);
    }

    [TestMethod]
    public void Line_StepsTowardCursor_AlongDominantCardinalAxis()
    {
        var origin = new Vector3Int(5, 5, 0);
        var cursorTile = new Vector3Int(9, 6, 0); // mostly horizontal (delta 4 vs delta 1) -- well outside the diagonal wedge
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Line, origin, cursorTile, range: 3, areaSize: 0, MapSize, tiles);

        CollectionAssert.AreEqual(new[]
        {
            new Vector3Int(6, 5, 0),
            new Vector3Int(7, 5, 0),
            new Vector3Int(8, 5, 0),
        }, tiles);
    }

    [TestMethod]
    public void Line_CursorAtFortyFiveDegrees_StepsDiagonally()
    {
        var origin = new Vector3Int(5, 5, 0);
        var cursorTile = new Vector3Int(8, 8, 0); // exactly 45 degrees
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Line, origin, cursorTile, range: 3, areaSize: 0, MapSize, tiles);

        CollectionAssert.AreEqual(new[]
        {
            new Vector3Int(6, 6, 0),
            new Vector3Int(7, 7, 0),
            new Vector3Int(8, 8, 0),
        }, tiles);
    }

    [TestMethod]
    public void Line_StopsEarly_AtMapEdge()
    {
        var origin = new Vector3Int(998, 5, 0);
        var cursorTile = new Vector3Int(999, 5, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Line, origin, cursorTile, range: 5, areaSize: 0, MapSize, tiles);

        Assert.HasCount(1, tiles);
        Assert.AreEqual(new Vector3Int(999, 5, 0), tiles[0]);
    }

    [TestMethod]
    public void Line_CursorOnOrigin_HasNoDirection_ReturnsNoTiles()
    {
        var origin = new Vector3Int(5, 5, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Line, origin, origin, range: 3, areaSize: 0, MapSize, tiles);

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void Cone_HitsTileDirectlyTowardCursor_ButNotTileDirectlyAwayFromIt()
    {
        var origin = new Vector3Int(10, 10, 0);
        var cursorTile = new Vector3Int(15, 10, 0); // due east
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Cone, origin, cursorTile, range: 3, areaSize: 0, MapSize, tiles);

        CollectionAssert.Contains(tiles, new Vector3Int(12, 10, 0), "Directly toward the cursor must be inside the cone.");
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(8, 10, 0), "Directly opposite the cursor direction must be outside the cone.");
        CollectionAssert.DoesNotContain(tiles, origin, "The caster's own tile has no direction and isn't part of a directional cone.");
    }

    [TestMethod]
    public void Cone_HitsOffAxisTileWithinHalfAngle_ButNotJustBeyondIt()
    {
        // Direction is due east (15,10). Offset (4,3) from origin is ~36.87 degrees off-axis
        // (inside the 45-degree half-angle); offset (3,4) is ~53.13 degrees off-axis (outside
        // it) -- both well clear of the exact 45-degree boundary, to avoid a floating-point-
        // precision-sensitive test.
        var origin = new Vector3Int(10, 10, 0);
        var cursorTile = new Vector3Int(15, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Cone, origin, cursorTile, range: 5, areaSize: 0, MapSize, tiles);

        CollectionAssert.Contains(tiles, new Vector3Int(14, 13, 0), "~37 degrees off-axis is within the 45-degree half-angle.");
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(13, 14, 0), "~53 degrees off-axis is outside the 45-degree half-angle.");
    }

    [TestMethod]
    public void Cone_CursorOnOrigin_HasNoDirection_ReturnsNoTiles()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Cone, origin, origin, range: 3, areaSize: 0, MapSize, tiles);

        Assert.IsEmpty(tiles);
    }
}
