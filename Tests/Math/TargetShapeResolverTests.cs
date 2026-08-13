using Engine.Math;

namespace Tests.Math;

[TestClass]
public sealed class TargetShapeResolverTests
{
    private static readonly Vector3Int MapSize = new(1000, 1000, 1);
    private static readonly Vector2Byte SingleTile = new(1, 1);

    [TestMethod]
    public void Adjacent_SingleTile_ExcludesOriginAndIncludesAllEightSurroundingNeighbors()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Adjacent, origin, SingleTile, cursorTile: origin, range: 0, areaSize: 0, MapSize, tiles);

        Assert.HasCount(8, tiles);
        CollectionAssert.DoesNotContain(tiles, origin);
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

        TargetShapeResolver.Resolve(TargetShape.Adjacent, origin, SingleTile, cursorTile: origin, range: 99, areaSize: 99, MapSize, tiles);

        Assert.HasCount(8, tiles);
    }

    [TestMethod]
    public void Adjacent_TwoByTwoFootprint_ResolvesTwelveTilePerimeterExcludingOwnFootprint()
    {
        var origin = new Vector3Int(10, 10, 0);
        var size = new Vector2Byte(2, 2);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Adjacent, origin, size, cursorTile: origin, range: 0, areaSize: 0, MapSize, tiles);

        Assert.HasCount(12, tiles);
        // Footprint occupies (10,10)-(11,11) -- none of those four tiles should appear.
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(10, 10, 0));
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(11, 10, 0));
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(10, 11, 0));
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(11, 11, 0));
        // Spot-check a few perimeter tiles, including a corner.
        CollectionAssert.Contains(tiles, new Vector3Int(9, 9, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(12, 12, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(9, 10, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(10, 12, 0));
    }

    [TestMethod]
    public void Adjacent_TwoByThreeFootprint_ResolvesFourteenTilePerimeter()
    {
        var origin = new Vector3Int(10, 10, 0);
        var size = new Vector2Byte(2, 3);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Adjacent, origin, size, cursorTile: origin, range: 0, areaSize: 0, MapSize, tiles);

        Assert.HasCount(14, tiles);
        for (var x = origin.X; x < origin.X + size.X; x++)
        {
            for (var y = origin.Y; y < origin.Y + size.Y; y++)
            {
                CollectionAssert.DoesNotContain(tiles, new Vector3Int(x, y, 0));
            }
        }
    }

    [TestMethod]
    public void AdjacentWithSelf_SingleTile_IncludesOriginAndAllEightSurroundingNeighbors()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.AdjacentWithSelf, origin, SingleTile, cursorTile: origin, range: 0, areaSize: 0, MapSize, tiles);

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
    public void AdjacentWithSelf_IgnoresRangeAndAreaSize_AlwaysFixedRadiusOne()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.AdjacentWithSelf, origin, SingleTile, cursorTile: origin, range: 99, areaSize: 99, MapSize, tiles);

        Assert.HasCount(9, tiles);
    }

    [TestMethod]
    public void AdjacentWithSelf_TwoByTwoFootprint_ResolvesPerimeterPlusOwnFootprint()
    {
        var origin = new Vector3Int(10, 10, 0);
        var size = new Vector2Byte(2, 2);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.AdjacentWithSelf, origin, size, cursorTile: origin, range: 0, areaSize: 0, MapSize, tiles);

        // 12-tile perimeter (see the plain-Adjacent equivalent test) plus the 4-tile footprint.
        Assert.HasCount(16, tiles);
        CollectionAssert.Contains(tiles, new Vector3Int(10, 10, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(11, 10, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(10, 11, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(11, 11, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(9, 9, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(12, 12, 0));
    }

    [TestMethod]
    public void Resolve_ReusedResultsBuffer_IsClearedEachCall()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int> { new(0, 0, 0), new(1, 1, 0) };

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, origin, SingleTile, cursorTile: new Vector3Int(50, 50, 0), range: 1, areaSize: 0, MapSize, tiles);

        Assert.IsEmpty(tiles, "Stale entries from a previous call must not survive into a call that resolves to nothing.");
    }

    [TestMethod]
    public void SingleTarget_WithinRange_ReturnsExactlyTheCursorTile()
    {
        var origin = new Vector3Int(0, 0, 0);
        var cursorTile = new Vector3Int(10, 0, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, origin, SingleTile, cursorTile, range: 10, areaSize: 0, MapSize, tiles);

        Assert.HasCount(1, tiles);
        Assert.AreEqual(cursorTile, tiles[0]);
    }

    [TestMethod]
    public void SingleTarget_BeyondRange_ReturnsNoTiles()
    {
        var origin = new Vector3Int(0, 0, 0);
        var cursorTile = new Vector3Int(11, 0, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, origin, SingleTile, cursorTile, range: 10, areaSize: 0, MapSize, tiles);

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void Burst_CursorBeyondCastRange_ReturnsNoTiles()
    {
        var origin = new Vector3Int(0, 0, 0);
        var cursorTile = new Vector3Int(11, 0, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Burst, origin, SingleTile, cursorTile, range: 10, areaSize: 2, MapSize, tiles);

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void Burst_WithinCastRange_ResolvesDiamondFootprintCenteredOnCursor_NotOnCaster()
    {
        var origin = new Vector3Int(0, 10, 0);
        var cursorTile = new Vector3Int(5, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Burst, origin, SingleTile, cursorTile, range: 10, areaSize: 1, MapSize, tiles);

        // areaSize 1 -> radius 1 diamond around the cursor tile: itself plus 4 cardinal neighbors.
        Assert.HasCount(5, tiles);
        CollectionAssert.Contains(tiles, cursorTile);
        CollectionAssert.DoesNotContain(tiles, origin);
    }

    [TestMethod]
    public void Burst_CenteredOnCastersOwnTile_StillIncludesCaster()
    {
        // No change for AOE abilities, even for a multi-tile caster -- Burst always resolves
        // from the single origin point, never the caster's footprint.
        var origin = new Vector3Int(10, 10, 0);
        var size = new Vector2Byte(2, 2);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Burst, origin, size, cursorTile: origin, range: 0, areaSize: 1, MapSize, tiles);

        CollectionAssert.Contains(tiles, origin, "A blast centered on the caster's own anchor tile must still be able to hit the caster.");
    }

    [TestMethod]
    public void Burst_ZeroAreaSize_DegeneratesToJustTheCursorTile()
    {
        var origin = new Vector3Int(0, 0, 0);
        var cursorTile = new Vector3Int(5, 0, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Burst, origin, SingleTile, cursorTile, range: 10, areaSize: 0, MapSize, tiles);

        Assert.HasCount(1, tiles);
        Assert.AreEqual(cursorTile, tiles[0]);
    }

    [TestMethod]
    public void Line_StepsTowardCursor_AlongTheExactBresenhamSlope()
    {
        var origin = new Vector3Int(5, 5, 0);
        var cursorTile = new Vector3Int(9, 6, 0); // shallow, but not axis-aligned -- the line must actually rise, not snap flat.
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Line, origin, SingleTile, cursorTile, range: 3, areaSize: 0, MapSize, tiles);

        CollectionAssert.AreEqual(new[]
        {
            new Vector3Int(6, 5, 0),
            new Vector3Int(7, 6, 0),
            new Vector3Int(8, 6, 0),
        }, tiles);
    }

    /// <summary>
    /// Two cursor angles shallow enough that the old 8-direction-snapped Line would have
    /// collapsed both onto the same "mostly horizontal" bucket and produced an identical straight
    /// line -- the whole point of switching Line to continuous Bresenham stepping (matching Cone)
    /// is that these must now diverge, tracing genuinely different paths.
    /// </summary>
    [TestMethod]
    public void Line_TwoShallowAngles_ThatWouldHavePreviouslySharedADirectionBucket_TraceDifferentPaths()
    {
        var origin = new Vector3Int(10, 10, 0);
        var shallowerCursor = new Vector3Int(18, 11, 0); // slope 1/8
        var steeperCursor = new Vector3Int(18, 13, 0); // slope 3/8 -- still well under the old 0.414 "mostly horizontal" cutoff

        var shallowerTiles = new List<Vector3Int>();
        var steeperTiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Line, origin, SingleTile, shallowerCursor, range: 6, areaSize: 0, MapSize, shallowerTiles);
        TargetShapeResolver.Resolve(TargetShape.Line, origin, SingleTile, steeperCursor, range: 6, areaSize: 0, MapSize, steeperTiles);

        CollectionAssert.AreNotEqual(shallowerTiles, steeperTiles, "Two meaningfully different shallow angles must not resolve to the same line.");
        CollectionAssert.AreEqual(new[]
        {
            new Vector3Int(11, 10, 0),
            new Vector3Int(12, 10, 0),
            new Vector3Int(13, 10, 0),
            new Vector3Int(14, 11, 0),
            new Vector3Int(15, 11, 0),
            new Vector3Int(16, 11, 0),
        }, shallowerTiles);
        CollectionAssert.AreEqual(new[]
        {
            new Vector3Int(11, 10, 0),
            new Vector3Int(12, 11, 0),
            new Vector3Int(13, 11, 0),
            new Vector3Int(14, 12, 0),
            new Vector3Int(15, 12, 0),
            new Vector3Int(16, 12, 0),
        }, steeperTiles);
    }

    [TestMethod]
    public void Line_CursorAtFortyFiveDegrees_StepsDiagonally()
    {
        var origin = new Vector3Int(5, 5, 0);
        var cursorTile = new Vector3Int(8, 8, 0); // exactly 45 degrees
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Line, origin, SingleTile, cursorTile, range: 3, areaSize: 0, MapSize, tiles);

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

        TargetShapeResolver.Resolve(TargetShape.Line, origin, SingleTile, cursorTile, range: 5, areaSize: 0, MapSize, tiles);

        Assert.HasCount(1, tiles);
        Assert.AreEqual(new Vector3Int(999, 5, 0), tiles[0]);
    }

    [TestMethod]
    public void Line_CursorOnOrigin_HasNoDirection_ReturnsNoTiles()
    {
        var origin = new Vector3Int(5, 5, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Line, origin, SingleTile, origin, range: 3, areaSize: 0, MapSize, tiles);

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void Line_MultiTileCaster_OriginatesFromFootprintCellClosestToCursor_AndExcludesOwnFootprint()
    {
        // 2x2 footprint at (10,10)-(11,11). Cursor due east of the footprint's right edge should
        // pick (11,10) or (11,11) as the closest cell (both share X=11, so the closest-point clamp
        // picks whichever Y is closer -- cursorTile.Y = 10 here, so (11,10)) and step east from there.
        var origin = new Vector3Int(10, 10, 0);
        var size = new Vector2Byte(2, 2);
        var cursorTile = new Vector3Int(20, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Line, origin, size, cursorTile, range: 3, areaSize: 0, MapSize, tiles);

        CollectionAssert.AreEqual(new[]
        {
            new Vector3Int(12, 10, 0),
            new Vector3Int(13, 10, 0),
            new Vector3Int(14, 10, 0),
        }, tiles);
        for (var x = origin.X; x < origin.X + size.X; x++)
        {
            for (var y = origin.Y; y < origin.Y + size.Y; y++)
            {
                CollectionAssert.DoesNotContain(tiles, new Vector3Int(x, y, 0));
            }
        }
    }

    [TestMethod]
    public void Cone_HitsTileDirectlyTowardCursor_ButNotTileDirectlyAwayFromIt()
    {
        var origin = new Vector3Int(10, 10, 0);
        var cursorTile = new Vector3Int(15, 10, 0); // due east
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Cone, origin, SingleTile, cursorTile, range: 3, areaSize: 0, MapSize, tiles);

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

        TargetShapeResolver.Resolve(TargetShape.Cone, origin, SingleTile, cursorTile, range: 5, areaSize: 0, MapSize, tiles);

        CollectionAssert.Contains(tiles, new Vector3Int(14, 13, 0), "~37 degrees off-axis is within the 45-degree half-angle.");
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(13, 14, 0), "~53 degrees off-axis is outside the 45-degree half-angle.");
    }

    [TestMethod]
    public void Cone_CursorOnOrigin_HasNoDirection_ReturnsNoTiles()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Cone, origin, SingleTile, origin, range: 3, areaSize: 0, MapSize, tiles);

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void Cone_MultiTileCaster_NeverIncludesAnyOfTheCastersOwnFootprint()
    {
        var origin = new Vector3Int(10, 10, 0);
        var size = new Vector2Byte(2, 2);
        var cursorTile = new Vector3Int(15, 10, 0); // due east of the footprint

        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Cone, origin, size, cursorTile, range: 5, areaSize: 0, MapSize, tiles);

        for (var x = origin.X; x < origin.X + size.X; x++)
        {
            for (var y = origin.Y; y < origin.Y + size.Y; y++)
            {
                CollectionAssert.DoesNotContain(tiles, new Vector3Int(x, y, 0));
            }
        }
        Assert.IsGreaterThan(0, tiles.Count, "Sanity check: the cone should still resolve real tiles beyond the caster's own footprint.");
    }
}
