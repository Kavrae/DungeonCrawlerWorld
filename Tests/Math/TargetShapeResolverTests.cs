using Engine.Math;
using Engine.Tiles;

namespace Tests.Math;

[TestClass]
public sealed class TargetShapeResolverTests
{
    private static readonly MapBounds Bounds = new(0, 0, 1000, 1000, 1);
    private static readonly Vector2Byte SingleTile = new(1, 1);

    [TestMethod]
    public void Adjacent_SingleTile_ExcludesOriginAndIncludesAllEightSurroundingNeighbors()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Adjacent, TargetAnchor.Caster, origin, SingleTile, aimedTile: origin, new TargetRanges(MaximumRange: 0, AreaSize: 0), Bounds, tiles);

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

        TargetShapeResolver.Resolve(TargetShape.Adjacent, TargetAnchor.Caster, origin, SingleTile, aimedTile: origin, new TargetRanges(MaximumRange: 99, AreaSize: 99), Bounds, tiles);

        Assert.HasCount(8, tiles);
    }

    [TestMethod]
    public void Adjacent_TwoByTwoFootprint_ResolvesTwelveTilePerimeterExcludingOwnFootprint()
    {
        var origin = new Vector3Int(10, 10, 0);
        var size = new Vector2Byte(2, 2);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Adjacent, TargetAnchor.Caster, origin, size, aimedTile: origin, new TargetRanges(MaximumRange: 0, AreaSize: 0), Bounds, tiles);

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

        TargetShapeResolver.Resolve(TargetShape.Adjacent, TargetAnchor.Caster, origin, size, aimedTile: origin, new TargetRanges(MaximumRange: 0, AreaSize: 0), Bounds, tiles);

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

        TargetShapeResolver.Resolve(TargetShape.Adjacent | TargetShape.Self, TargetAnchor.Caster, origin, SingleTile, aimedTile: origin, new TargetRanges(MaximumRange: 0, AreaSize: 0), Bounds, tiles);

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

        TargetShapeResolver.Resolve(TargetShape.Adjacent | TargetShape.Self, TargetAnchor.Caster, origin, SingleTile, aimedTile: origin, new TargetRanges(MaximumRange: 99, AreaSize: 99), Bounds, tiles);

        Assert.HasCount(9, tiles);
    }

    [TestMethod]
    public void AdjacentWithSelf_TwoByTwoFootprint_ResolvesPerimeterPlusOwnFootprint()
    {
        var origin = new Vector3Int(10, 10, 0);
        var size = new Vector2Byte(2, 2);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Adjacent | TargetShape.Self, TargetAnchor.Caster, origin, size, aimedTile: origin, new TargetRanges(MaximumRange: 0, AreaSize: 0), Bounds, tiles);

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

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, TargetAnchor.PathEnd, origin, SingleTile, aimedTile: new Vector3Int(50, 50, 0), new TargetRanges(MaximumRange: 1, AreaSize: 0), Bounds, tiles);

        Assert.IsEmpty(tiles, "Stale entries from a previous call must not survive into a call that resolves to nothing.");
    }

    [TestMethod]
    public void SingleTarget_WithinRange_ReturnsExactlyTheCursorTile()
    {
        var origin = new Vector3Int(0, 0, 0);
        var cursorTile = new Vector3Int(10, 0, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, TargetAnchor.PathEnd, origin, SingleTile, cursorTile, new TargetRanges(MaximumRange: 10, AreaSize: 0), Bounds, tiles);

        Assert.HasCount(1, tiles);
        Assert.AreEqual(cursorTile, tiles[0]);
    }

    [TestMethod]
    public void SingleTarget_BeyondRange_ReturnsNoTiles()
    {
        var origin = new Vector3Int(0, 0, 0);
        var cursorTile = new Vector3Int(11, 0, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, TargetAnchor.PathEnd, origin, SingleTile, cursorTile, new TargetRanges(MaximumRange: 10, AreaSize: 0), Bounds, tiles);

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void Burst_CursorBeyondCastRange_ReturnsNoTiles()
    {
        var origin = new Vector3Int(0, 0, 0);
        var cursorTile = new Vector3Int(11, 0, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Circle, TargetAnchor.PathEnd, origin, SingleTile, cursorTile, new TargetRanges(MaximumRange: 10, AreaSize: 2), Bounds, tiles);

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void Burst_WithinCastRange_ResolvesRoundedCircleCenteredOnCursor_NotOnCaster()
    {
        var origin = new Vector3Int(0, 10, 0);
        var cursorTile = new Vector3Int(5, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Circle, TargetAnchor.PathEnd, origin, SingleTile, cursorTile, new TargetRanges(MaximumRange: 10, AreaSize: 2), Bounds, tiles);

        Assert.HasCount(21, tiles, "A radius-2 rounded circle is the 5x5 block without its four corners.");
        CollectionAssert.Contains(tiles, cursorTile);
        CollectionAssert.Contains(tiles, new Vector3Int(7, 11, 0));
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(7, 12, 0));
        CollectionAssert.DoesNotContain(tiles, origin);
    }

    [TestMethod]
    public void Burst_CenteredOnCastersOwnTile_StillIncludesCaster()
    {
        var origin = new Vector3Int(10, 10, 0);
        var size = new Vector2Byte(2, 2);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Circle, TargetAnchor.PathEnd, origin, size, aimedTile: origin, new TargetRanges(MaximumRange: 0, AreaSize: 1), Bounds, tiles);

        CollectionAssert.Contains(tiles, origin, "A blast centered on the caster's own anchor tile must still be able to hit the caster.");
    }

    [TestMethod]
    public void Burst_ZeroAreaSize_DegeneratesToJustTheCursorTile()
    {
        var origin = new Vector3Int(0, 0, 0);
        var cursorTile = new Vector3Int(5, 0, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Circle, TargetAnchor.PathEnd, origin, SingleTile, cursorTile, new TargetRanges(MaximumRange: 10, AreaSize: 0), Bounds, tiles);

        Assert.HasCount(1, tiles);
        Assert.AreEqual(cursorTile, tiles[0]);
    }

    [TestMethod]
    public void Line_StepsTowardCursor_AlongTheExactBresenhamSlope()
    {
        var origin = new Vector3Int(5, 5, 0);
        var cursorTile = new Vector3Int(9, 6, 0); // shallow, but not axis-aligned -- the line must actually rise, not snap flat.
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Line, TargetAnchor.Caster, origin, SingleTile, cursorTile, new TargetRanges(MaximumRange: 3, AreaSize: 3), Bounds, tiles);

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

        TargetShapeResolver.Resolve(TargetShape.Line, TargetAnchor.Caster, origin, SingleTile, shallowerCursor, new TargetRanges(MaximumRange: 6, AreaSize: 6), Bounds, shallowerTiles);
        TargetShapeResolver.Resolve(TargetShape.Line, TargetAnchor.Caster, origin, SingleTile, steeperCursor, new TargetRanges(MaximumRange: 6, AreaSize: 6), Bounds, steeperTiles);

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

        // (3, 3) is within a rounded range of 4 but not 3; (4, 4) is past 4.
        TargetShapeResolver.Resolve(TargetShape.Line, TargetAnchor.Caster, origin, SingleTile, cursorTile, new TargetRanges(MaximumRange: 4, AreaSize: 4), Bounds, tiles);

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

        TargetShapeResolver.Resolve(TargetShape.Line, TargetAnchor.Caster, origin, SingleTile, cursorTile, new TargetRanges(MaximumRange: 5, AreaSize: 5), Bounds, tiles);

        Assert.HasCount(1, tiles);
        Assert.AreEqual(new Vector3Int(999, 5, 0), tiles[0]);
    }

    [TestMethod]
    public void Line_CursorOnOrigin_HasNoDirection_ReturnsNoTiles()
    {
        var origin = new Vector3Int(5, 5, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Line, TargetAnchor.Caster, origin, SingleTile, origin, new TargetRanges(MaximumRange: 3, AreaSize: 3), Bounds, tiles);

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

        TargetShapeResolver.Resolve(TargetShape.Line, TargetAnchor.Caster, origin, size, cursorTile, new TargetRanges(MaximumRange: 3, AreaSize: 3), Bounds, tiles);

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

        TargetShapeResolver.Resolve(TargetShape.Cone, TargetAnchor.Caster, origin, SingleTile, cursorTile, new TargetRanges(MaximumRange: 3, AreaSize: 3), Bounds, tiles);

        CollectionAssert.Contains(tiles, new Vector3Int(12, 10, 0), "Directly toward the cursor must be inside the cone.");
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(8, 10, 0), "Directly opposite the cursor direction must be outside the cone.");
        CollectionAssert.DoesNotContain(tiles, origin, "The caster's own tile has no direction and isn't part of a directional cone.");
    }

    [TestMethod]
    public void Cone_HitsOffAxisTileWithinHalfAngle_ButNotJustBeyondIt()
    {
        // Direction is due east (15,10). The half-angle is arctan(1/2), ~26.57 degrees: offset
        // (4,2) is exactly on it (inside, the edge counts), (5,1) is ~11.3 degrees off-axis, and
        // (4,3) is ~36.87 degrees off-axis (outside).
        var origin = new Vector3Int(10, 10, 0);
        var cursorTile = new Vector3Int(15, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Cone, TargetAnchor.Caster, origin, SingleTile, cursorTile, new TargetRanges(MaximumRange: 5, AreaSize: 5), Bounds, tiles);

        CollectionAssert.Contains(tiles, new Vector3Int(15, 11, 0), "~11 degrees off-axis is within the half-angle.");
        CollectionAssert.Contains(tiles, new Vector3Int(14, 12, 0), "Exactly on the half-angle is inside.");
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(14, 13, 0), "~37 degrees off-axis is outside the half-angle.");
    }

    [TestMethod]
    public void Cone_CursorOnOrigin_HasNoDirection_ReturnsNoTiles()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Cone, TargetAnchor.Caster, origin, SingleTile, origin, new TargetRanges(MaximumRange: 3, AreaSize: 3), Bounds, tiles);

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void Cone_MultiTileCaster_NeverIncludesAnyOfTheCastersOwnFootprint()
    {
        var origin = new Vector3Int(10, 10, 0);
        var size = new Vector2Byte(2, 2);
        var cursorTile = new Vector3Int(15, 10, 0); // due east of the footprint

        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Cone, TargetAnchor.Caster, origin, size, cursorTile, new TargetRanges(MaximumRange: 5, AreaSize: 5), Bounds, tiles);

        for (var x = origin.X; x < origin.X + size.X; x++)
        {
            for (var y = origin.Y; y < origin.Y + size.Y; y++)
            {
                CollectionAssert.DoesNotContain(tiles, new Vector3Int(x, y, 0));
            }
        }
        Assert.IsGreaterThan(0, tiles.Count, "Sanity check: the cone should still resolve real tiles beyond the caster's own footprint.");
    }

    [TestMethod]
    public void Cone_FarEdgeIsAnArc_NotTheSquaresCorners()
    {
        var origin = new Vector3Int(10, 10, 0);
        var cursorTile = new Vector3Int(14, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Cone, TargetAnchor.Caster, origin, SingleTile, cursorTile, new TargetRanges(MaximumRange: 4, AreaSize: 4), Bounds, tiles);

        CollectionAssert.Contains(tiles, cursorTile);
        CollectionAssert.Contains(tiles, new Vector3Int(14, 12, 0));
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(14, 13, 0));
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(14, 14, 0));
    }

    [TestMethod]
    public void ConeLineSingleTarget_Combined_ResolvesToExactlyConesOwnOutput()
    {
        // Off-axis, non-45-degree direction and a mid-sized range -- not a boundary case, just a
        // representative check that the redundant-resolve shortcut (SingleTarget <= Line <= Cone)
        // holds, per TargetShapeResolver's own doc comment.
        var origin = new Vector3Int(10, 10, 0);
        var cursorTile = new Vector3Int(15, 13, 0);
        const int range = 5;

        var coneOnly = new List<Vector3Int>();
        TargetShapeResolver.Resolve(TargetShape.Cone, TargetAnchor.Caster, origin, SingleTile, cursorTile, new TargetRanges(MaximumRange: range, AreaSize: range), Bounds, coneOnly);

        var combined = new List<Vector3Int>();
        TargetShapeResolver.Resolve(TargetShape.Cone | TargetShape.Line | TargetShape.SingleTarget, TargetAnchor.Caster, origin, SingleTile, cursorTile, new TargetRanges(MaximumRange: range, AreaSize: range), Bounds, combined);

        CollectionAssert.AreEqual(coneOnly, combined);
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(2, 2)]
    public void Line_IsInsideTheSameAimsCone_ForEveryAimAndRange(int width, int height)
    {
        var origin = new Vector3Int(20, 20, 0);
        var size = new Vector2Byte((byte)width, (byte)height);
        var lineTiles = new List<Vector3Int>();
        var coneTiles = new List<Vector3Int>();

        for (var range = 1; range <= 8; range++)
        {
            for (var y = origin.Y - 9; y <= origin.Y + 9; y++)
            {
                for (var x = origin.X - 9; x <= origin.X + 9; x++)
                {
                    var cursorTile = new Vector3Int(x, y, 0);
                    TargetShapeResolver.Resolve(TargetShape.Line, TargetAnchor.Caster, origin, size, cursorTile, new TargetRanges(MaximumRange: range, AreaSize: range), Bounds, lineTiles);
                    TargetShapeResolver.Resolve(TargetShape.Cone, TargetAnchor.Caster, origin, size, cursorTile, new TargetRanges(MaximumRange: range, AreaSize: range), Bounds, coneTiles);

                    Assert.IsTrue(lineTiles.All(coneTiles.Contains), $"Range {range}, aimed at {cursorTile}.");
                }
            }
        }
    }

    [TestMethod]
    public void Cone_IsAsWideAtEachDistanceAsThatDistance()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Cone, TargetAnchor.Caster, origin, SingleTile, new Vector3Int(16, 10, 0), new TargetRanges(MaximumRange: 6, AreaSize: 6), Bounds, tiles);

        var rowsFourOut = tiles.Where(static tile => tile.X == 14).Select(static tile => tile.Y).OrderBy(static y => y).ToList();
        CollectionAssert.AreEqual(new[] { 8, 9, 10, 11, 12 }, rowsFourOut, "Four tiles out, the cone spans four tiles: two either side of the aim, edges included.");
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(11, 11, 0), "The diagonal neighbour is 45 degrees off the aim, outside the cone.");
    }

    [TestMethod]
    public void SingleTarget_RangeOne_AcceptsTheDiagonalNeighbor()
    {
        var origin = new Vector3Int(10, 10, 0);
        var cursorTile = new Vector3Int(11, 11, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, TargetAnchor.PathEnd, origin, SingleTile, cursorTile, new TargetRanges(MaximumRange: 1, AreaSize: 0), Bounds, tiles);

        Assert.HasCount(1, tiles);
        Assert.AreEqual(cursorTile, tiles[0]);
    }

    [TestMethod]
    public void SingleTarget_RangeTwo_AcceptsAKnightsMoveButNotTheDiagonalCorner()
    {
        var origin = new Vector3Int(10, 10, 0);
        var knightsMove = new List<Vector3Int>();
        var diagonalCorner = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, TargetAnchor.PathEnd, origin, SingleTile, new Vector3Int(12, 11, 0), new TargetRanges(MaximumRange: 2, AreaSize: 0), Bounds, knightsMove);
        TargetShapeResolver.Resolve(TargetShape.SingleTarget, TargetAnchor.PathEnd, origin, SingleTile, new Vector3Int(12, 12, 0), new TargetRanges(MaximumRange: 2, AreaSize: 0), Bounds, diagonalCorner);

        Assert.HasCount(1, knightsMove);
        Assert.IsEmpty(diagonalCorner);
    }

    [TestMethod]
    public void SingleTarget_MultiTileCaster_MeasuresRangeFromTheNearestFootprintTile()
    {
        var origin = new Vector3Int(10, 10, 0);
        var size = new Vector2Byte(3, 3);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, TargetAnchor.PathEnd, origin, size, new Vector3Int(17, 12, 0), new TargetRanges(MaximumRange: 5, AreaSize: 0), Bounds, tiles);

        Assert.HasCount(1, tiles);
    }

    [TestMethod]
    public void CombinedFlags_OverlappingGroups_DeduplicatesSharedTiles()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        // SingleTarget contributes {origin} (cursor == origin, distance 0). Burst (areaSize 1)
        // covers the 3x3 block centered on that same cursor tile, which also includes origin
        // itself -- exercising the exact overlap Resolve's own de-dup pass exists for.
        TargetShapeResolver.Resolve(TargetShape.SingleTarget | TargetShape.Circle, TargetAnchor.PathEnd, origin, SingleTile, aimedTile: origin, new TargetRanges(MaximumRange: 5, AreaSize: 1), Bounds, tiles);

        Assert.AreEqual(1, tiles.Count(tile => tile == origin), "origin is produced by both SingleTarget and Burst -- must appear exactly once after de-duplication.");
        Assert.HasCount(9, tiles, "SingleTarget contributes nothing new (already inside Burst's own radius-1 block); total should just be Burst's own 9 tiles.");
    }

    [TestMethod]
    public void Adjacent_AcrossZeroOnANegativeMap_KeepsEveryNeighbor()
    {
        var origin = new Vector3Int(-1, 0, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Adjacent, TargetAnchor.Caster, origin, SingleTile, aimedTile: origin, new TargetRanges(MaximumRange: 0, AreaSize: 0), new MapBounds(-1024, -1024, 1024, 1024, 1), tiles);

        Assert.HasCount(8, tiles);
        CollectionAssert.Contains(tiles, new Vector3Int(-2, -1, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(0, 1, 0));
    }

    /// <summary>Clipping is to the map's own minimum, not to 0: the burst loses only the tiles west of -1024.</summary>
    [TestMethod]
    public void Burst_AtANegativeMapEdge_ClipsAtTheMinimumNotZero()
    {
        var cursorTile = new Vector3Int(-1024, -500, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Circle, TargetAnchor.PathEnd, cursorTile, SingleTile, cursorTile, new TargetRanges(MaximumRange: 10, AreaSize: 1), new MapBounds(-1024, -1024, 1024, 1024, 1), tiles);

        Assert.HasCount(6, tiles);
        Assert.IsTrue(tiles.All(static tile => tile.X >= -1024));
        CollectionAssert.Contains(tiles, new Vector3Int(-1023, -500, 0));
    }

    [TestMethod]
    [DataRow(TargetShape.Line, 1, 1, 3)]
    [DataRow(TargetShape.Line, 2, 2, 3)]
    [DataRow(TargetShape.Line, 1, 1, 8)]
    [DataRow(TargetShape.Cone, 1, 1, 3)]
    [DataRow(TargetShape.Cone, 2, 2, 3)]
    [DataRow(TargetShape.Cone, 1, 1, 8)]
    [DataRow(TargetShape.SingleTarget, 1, 1, 1)]
    [DataRow(TargetShape.SingleTarget, 1, 1, 3)]
    [DataRow(TargetShape.SingleTarget, 2, 2, 3)]
    public void Reachable_IsEveryTileSomeAimReaches(TargetShape shape, int width, int height, int range)
    {
        var origin = new Vector3Int(20, 20, 0);
        var size = new Vector2Byte((byte)width, (byte)height);
        var isDirectional = (shape & (TargetShape.Line | TargetShape.Cone)) != 0;
        var anchor = isDirectional ? TargetAnchor.Caster : TargetAnchor.PathEnd;
        var ranges = new TargetRanges(MaximumRange: range, AreaSize: isDirectional ? range : 0);
        var everyAimReaches = new HashSet<Vector3Int>();
        var aimTiles = new List<Vector3Int>();
        for (var y = origin.Y - 10; y <= origin.Y + 10; y++)
        {
            for (var x = origin.X - 10; x <= origin.X + 10; x++)
            {
                TargetShapeResolver.Resolve(shape, anchor, origin, size, new Vector3Int(x, y, 0), ranges, Bounds, aimTiles);
                everyAimReaches.UnionWith(aimTiles);
            }
        }

        var reachable = new List<Vector3Int>();
        TargetShapeResolver.ResolveReachable(shape, anchor, origin, size, ranges, Bounds, reachable);

        Assert.HasCount(reachable.Count, reachable.Distinct().ToList());
        Assert.IsTrue(everyAimReaches.SetEquals(reachable));
    }

    [TestMethod]
    public void Reachable_Line_IsARoundedCircleWithoutTheCaster()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.ResolveReachable(TargetShape.Line, TargetAnchor.Caster, origin, SingleTile, new TargetRanges(MaximumRange: 3, AreaSize: 3), Bounds, tiles);

        Assert.HasCount(36, tiles);
        CollectionAssert.Contains(tiles, new Vector3Int(13, 10, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(12, 12, 0));
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(13, 13, 0));
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(14, 10, 0));
        CollectionAssert.DoesNotContain(tiles, origin);
    }

    [TestMethod]
    public void Reachable_Burst_IsTheAnchorsInRange()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.ResolveReachable(TargetShape.Circle, TargetAnchor.PathEnd, origin, SingleTile, new TargetRanges(MaximumRange: 2, AreaSize: 0), Bounds, tiles);

        Assert.HasCount(21, tiles);
        CollectionAssert.Contains(tiles, origin);
        CollectionAssert.Contains(tiles, new Vector3Int(12, 11, 0));
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(12, 12, 0));
    }

    [TestMethod]
    public void Reachable_Cone_AtTheMapEdge_StaysOnTheMap()
    {
        var origin = new Vector3Int(1, 1, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.ResolveReachable(TargetShape.Cone, TargetAnchor.Caster, origin, SingleTile, new TargetRanges(MaximumRange: 3, AreaSize: 3), Bounds, tiles);

        Assert.HasCount(21, tiles);
        Assert.IsTrue(tiles.All(static tile => tile.X >= 0 && tile.Y >= 0));
    }

    [TestMethod]
    public void Circle_AtCaster_IsANovaWithoutTheCaster()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Circle, TargetAnchor.Caster, origin, SingleTile, aimedTile: new Vector3Int(30, 30, 0), new TargetRanges(MaximumRange: 5, AreaSize: 1), Bounds, tiles);

        Assert.HasCount(8, tiles);
        CollectionAssert.DoesNotContain(tiles, origin);
    }

    [TestMethod]
    public void SingleTarget_AtCaster_CoversNothing()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, TargetAnchor.Caster, origin, SingleTile, aimedTile: new Vector3Int(12, 10, 0), new TargetRanges(MaximumRange: 5), Bounds, tiles);

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void Self_AtPathEnd_CoversNothing()
    {
        var origin = new Vector3Int(10, 10, 0);
        var aimedTile = new Vector3Int(13, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Self, TargetAnchor.PathEnd, origin, SingleTile, aimedTile, new TargetRanges(MaximumRange: 5), Bounds, tiles);

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void Adjacent_AtPathEnd_IsTheRingAroundTheEndTile()
    {
        var origin = new Vector3Int(10, 10, 0);
        var aimedTile = new Vector3Int(14, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Adjacent, TargetAnchor.PathEnd, origin, SingleTile, aimedTile, new TargetRanges(MaximumRange: 5), Bounds, tiles);

        Assert.HasCount(8, tiles);
        CollectionAssert.DoesNotContain(tiles, aimedTile);
        CollectionAssert.Contains(tiles, new Vector3Int(13, 10, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(15, 11, 0));
    }

    [TestMethod]
    public void Line_AtPathEnd_StartsAtTheEndTileAndCarriesOnPastIt()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Line, TargetAnchor.PathEnd, origin, SingleTile, aimedTile: new Vector3Int(13, 10, 0), new TargetRanges(MaximumRange: 5, AreaSize: 2), Bounds, tiles);

        CollectionAssert.AreEqual(new[] { new Vector3Int(13, 10, 0), new Vector3Int(14, 10, 0), new Vector3Int(15, 10, 0) }, tiles);
    }

    [TestMethod]
    public void Line_AtPathEnd_FollowsTheCasterToAimLine()
    {
        var origin = new Vector3Int(10, 10, 0);
        var aimedTile = new Vector3Int(14, 12, 0);
        var casterLine = new List<Vector3Int>();
        var endLine = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Line, TargetAnchor.Caster, origin, SingleTile, aimedTile, new TargetRanges(MaximumRange: 10, AreaSize: 9), Bounds, casterLine);
        TargetShapeResolver.Resolve(TargetShape.Line, TargetAnchor.PathEnd, origin, SingleTile, aimedTile, new TargetRanges(MaximumRange: 10, AreaSize: 3), Bounds, endLine);

        Assert.AreEqual(aimedTile, endLine[0]);
        Assert.IsGreaterThan(1, endLine.Count);
        CollectionAssert.IsSubsetOf(endLine, casterLine);
    }

    [TestMethod]
    public void Cone_AtPathEnd_FansOutFromTheEndTileAwayFromTheCaster()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Cone, TargetAnchor.PathEnd, origin, SingleTile, aimedTile: new Vector3Int(13, 10, 0), new TargetRanges(MaximumRange: 5, AreaSize: 2), Bounds, tiles);

        CollectionAssert.AreEquivalent(
            new[] { new Vector3Int(13, 10, 0), new Vector3Int(14, 10, 0), new Vector3Int(15, 10, 0), new Vector3Int(15, 9, 0), new Vector3Int(15, 11, 0) },
            tiles);
    }

    [TestMethod]
    public void DirectionalShapes_AtPathEnd_AimedAtTheCaster_CoverNothing()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Cone | TargetShape.Line, TargetAnchor.PathEnd, origin, SingleTile, aimedTile: origin, new TargetRanges(MaximumRange: 5, AreaSize: 3), Bounds, tiles);

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void Circle_AtPathEnd_OverTheCaster_IncludesTheCaster()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Circle, TargetAnchor.PathEnd, origin, SingleTile, aimedTile: new Vector3Int(11, 10, 0), new TargetRanges(MaximumRange: 5, AreaSize: 1), Bounds, tiles);

        CollectionAssert.Contains(tiles, origin);
    }

    [TestMethod]
    public void MinimumRange_AtPathEnd_RefusesAnAimNearerThanIt()
    {
        var origin = new Vector3Int(10, 10, 0);
        var ranges = new TargetRanges(MaximumRange: 5, MinimumRange: 2);
        var tooNear = new List<Vector3Int>();
        var farEnough = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, TargetAnchor.PathEnd, origin, SingleTile, new Vector3Int(11, 11, 0), ranges, Bounds, tooNear);
        TargetShapeResolver.Resolve(TargetShape.SingleTarget, TargetAnchor.PathEnd, origin, SingleTile, new Vector3Int(12, 10, 0), ranges, Bounds, farEnough);

        Assert.IsEmpty(tooNear);
        CollectionAssert.AreEqual(new[] { new Vector3Int(12, 10, 0) }, farEnough);
    }

    [TestMethod]
    public void MinimumRange_AtCaster_LeavesOutTheShapesNearTiles()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Line, TargetAnchor.Caster, origin, SingleTile, aimedTile: new Vector3Int(15, 10, 0), new TargetRanges(MaximumRange: 5, AreaSize: 5, MinimumRange: 3), Bounds, tiles);

        CollectionAssert.AreEqual(new[] { new Vector3Int(13, 10, 0), new Vector3Int(14, 10, 0), new Vector3Int(15, 10, 0) }, tiles);
    }

    [TestMethod]
    public void MinimumRange_Reachable_IsABand()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.ResolveReachable(TargetShape.Circle, TargetAnchor.PathEnd, origin, SingleTile, new TargetRanges(MaximumRange: 3, AreaSize: 2, MinimumRange: 2), Bounds, tiles);

        Assert.HasCount(37 - 9, tiles);
        Assert.IsTrue(tiles.All(tile => GridDistance.ChebyshevDistance(origin, tile) >= 2));
        CollectionAssert.Contains(tiles, new Vector3Int(13, 10, 0));
    }

    [TestMethod]
    public void MinimumRange_Reachable_TwoByTwoCaster_MeasuresFromTheNearestFootprintTile()
    {
        var origin = new Vector3Int(10, 10, 0);
        var size = new Vector2Byte(2, 2);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.ResolveReachable(TargetShape.SingleTarget, TargetAnchor.PathEnd, origin, size, new TargetRanges(MaximumRange: 3, MinimumRange: 2), Bounds, tiles);

        CollectionAssert.DoesNotContain(tiles, new Vector3Int(12, 11, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(13, 11, 0));
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(11, 11, 0));
    }

    [TestMethod]
    public void IsAimIndependent_OnlyForUndirectedShapesAtTheCaster()
    {
        Assert.IsTrue(TargetShapeResolver.IsAimIndependent(TargetShape.Adjacent | TargetShape.Self, TargetAnchor.Caster));
        Assert.IsTrue(TargetShapeResolver.IsAimIndependent(TargetShape.Circle, TargetAnchor.Caster));
        Assert.IsFalse(TargetShapeResolver.IsAimIndependent(TargetShape.Cone, TargetAnchor.Caster));
        Assert.IsFalse(TargetShapeResolver.IsAimIndependent(TargetShape.Self, TargetAnchor.PathEnd));
    }

    [TestMethod]
    public void ClampToRangeBand_InTheBand_IsTheTarget()
    {
        var origin = new Vector3Int(10, 10, 0);
        var target = new Vector3Int(13, 11, 0);

        Assert.AreEqual(target, TargetShapeResolver.ClampToRangeBand(origin, SingleTile, target, new TargetRanges(MaximumRange: 5, MinimumRange: 2)));
    }

    [TestMethod]
    public void ClampToRangeBand_PastMaximum_IsTheFarthestTileInRangeTowardIt()
    {
        var origin = new Vector3Int(10, 10, 0);

        Assert.AreEqual(new Vector3Int(15, 10, 0), TargetShapeResolver.ClampToRangeBand(origin, SingleTile, new Vector3Int(20, 10, 0), new TargetRanges(MaximumRange: 5)));
    }

    [TestMethod]
    public void ClampToRangeBand_NearerThanMinimum_IsTheFirstTileFarEnoughPastIt()
    {
        var origin = new Vector3Int(10, 10, 0);

        Assert.AreEqual(new Vector3Int(13, 10, 0), TargetShapeResolver.ClampToRangeBand(origin, SingleTile, new Vector3Int(11, 10, 0), new TargetRanges(MaximumRange: 5, MinimumRange: 3)));
    }

    [TestMethod]
    public void ClampToRangeBand_OnTheFootprint_IsTheTarget()
    {
        var origin = new Vector3Int(10, 10, 0);

        Assert.AreEqual(origin, TargetShapeResolver.ClampToRangeBand(origin, SingleTile, origin, new TargetRanges(MaximumRange: 5, MinimumRange: 2)));
    }

    [TestMethod]
    public void None_CoversNothing()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.None, TargetAnchor.PathEnd, origin, SingleTile, new Vector3Int(13, 10, 0), new TargetRanges(MaximumRange: 5, AreaSize: 3), Bounds, tiles);

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void Wall_OddLength_IsCentredAcrossTheAim()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Wall, TargetAnchor.PathEnd, origin, SingleTile, new Vector3Int(15, 10, 0), new TargetRanges(MaximumRange: 10, AreaSize: 9), Bounds, tiles);

        CollectionAssert.AreEquivalent(Enumerable.Range(6, 9).Select(static y => new Vector3Int(15, y, 0)).ToList(), tiles);
    }

    [TestMethod]
    public void Wall_EvenLength_HasOneMoreTileOnOneSide()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Wall, TargetAnchor.PathEnd, origin, SingleTile, new Vector3Int(10, 15, 0), new TargetRanges(MaximumRange: 10, AreaSize: 4), Bounds, tiles);

        Assert.HasCount(4, tiles);
        Assert.IsTrue(tiles.All(static tile => tile.Y == 15));
        CollectionAssert.Contains(tiles, new Vector3Int(10, 15, 0));
        Assert.AreEqual(3, tiles.Max(static tile => tile.X) - tiles.Min(static tile => tile.X));
    }

    [TestMethod]
    public void Wall_AcrossADiagonalAim_IsADiagonalLineTouchingAtCorners()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Wall, TargetAnchor.PathEnd, origin, SingleTile, new Vector3Int(14, 14, 0), new TargetRanges(MaximumRange: 10, AreaSize: 5), Bounds, tiles);

        CollectionAssert.AreEquivalent(
            new[] { new Vector3Int(12, 16, 0), new Vector3Int(13, 15, 0), new Vector3Int(14, 14, 0), new Vector3Int(15, 13, 0), new Vector3Int(16, 12, 0) },
            tiles);
    }

    [TestMethod]
    public void Wall_SteepAim_StaysOneTileThickAndContinuous()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Wall, TargetAnchor.PathEnd, origin, SingleTile, new Vector3Int(13, 11, 0), new TargetRanges(MaximumRange: 10, AreaSize: 9), Bounds, tiles);

        Assert.HasCount(9, tiles);
        var byY = tiles.OrderBy(static tile => tile.Y).ToList();
        for (var index = 1; index < byY.Count; index++)
        {
            Assert.AreEqual(1, GridDistance.ChebyshevDistance(byY[index - 1], byY[index]));
        }
    }

    [TestMethod]
    public void Ring_IsTheCirclesOutermostBand()
    {
        var origin = new Vector3Int(10, 10, 0);
        var aimedTile = new Vector3Int(20, 20, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Ring, TargetAnchor.PathEnd, origin, SingleTile, aimedTile, new TargetRanges(MaximumRange: 20, AreaSize: 3), Bounds, tiles);

        Assert.HasCount(37 - 21, tiles);
        CollectionAssert.DoesNotContain(tiles, aimedTile);
        CollectionAssert.Contains(tiles, new Vector3Int(23, 20, 0));
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(22, 20, 0));
    }

    [TestMethod]
    public void Ring_RadiusOne_IsTheAdjacentRing()
    {
        var origin = new Vector3Int(10, 10, 0);
        var aimedTile = new Vector3Int(14, 10, 0);
        var ring = new List<Vector3Int>();
        var adjacent = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Ring, TargetAnchor.PathEnd, origin, SingleTile, aimedTile, new TargetRanges(MaximumRange: 5, AreaSize: 1), Bounds, ring);
        TargetShapeResolver.Resolve(TargetShape.Adjacent, TargetAnchor.PathEnd, origin, SingleTile, aimedTile, new TargetRanges(MaximumRange: 5), Bounds, adjacent);

        CollectionAssert.AreEquivalent(adjacent, ring);
    }

    [TestMethod]
    public void Square_IsEveryTileWithinItsHalfSide()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Square, TargetAnchor.PathEnd, origin, SingleTile, new Vector3Int(14, 10, 0), new TargetRanges(MaximumRange: 5, AreaSize: 2), Bounds, tiles);

        Assert.HasCount(25, tiles);
        Assert.IsTrue(tiles.All(static tile => GridDistance.ChebyshevDistance(new Vector3Int(14, 10, 0), tile) <= 2));
    }

    [TestMethod]
    public void Square_AtCaster_LeavesOutTheCastersFootprint()
    {
        var origin = new Vector3Int(10, 10, 0);
        var size = new Vector2Byte(2, 2);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Square, TargetAnchor.Caster, origin, size, new Vector3Int(20, 20, 0), new TargetRanges(MaximumRange: 5, AreaSize: 1), Bounds, tiles);

        Assert.HasCount(16 - 4, tiles);
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(11, 11, 0));
    }

    [TestMethod]
    public void Cross_IsAPlusSignOfItsArmLength()
    {
        var origin = new Vector3Int(10, 10, 0);
        var centre = new Vector3Int(14, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Cross, TargetAnchor.PathEnd, origin, SingleTile, centre, new TargetRanges(MaximumRange: 5, AreaSize: 2), Bounds, tiles);

        Assert.HasCount(9, tiles);
        Assert.IsTrue(tiles.All(tile => (tile.X == centre.X || tile.Y == centre.Y) && GridDistance.ChebyshevDistance(centre, tile) <= 2));
    }

    [TestMethod]
    public void ResolveReachable_APathThatWallsStopOrTurn_LeavesOutTheTilesBehindAWall()
    {
        var origin = new Vector3Int(10, 10, 0);
        var lineOfSight = new TargetLineOfSight(new WallTiles((13, 10)));
        var tiles = new List<Vector3Int>();

        foreach (var pathWalls in new[] { PathWallRule.BouncesOffWalls, PathWallRule.StopsAtWall })
        {
            TargetShapeResolver.ResolveReachable(TargetShape.SingleTarget, TargetAnchor.PathEnd, origin, SingleTile, new TargetRanges(MaximumRange: 6), Bounds, tiles, lineOfSight, TargetPath.Projectile, pathWalls);

            CollectionAssert.Contains(tiles, new Vector3Int(12, 10, 0), $"{pathWalls}: in front of the wall.");
            CollectionAssert.DoesNotContain(tiles, new Vector3Int(13, 10, 0), $"{pathWalls}: the wall.");
            CollectionAssert.DoesNotContain(tiles, new Vector3Int(15, 10, 0), $"{pathWalls}: behind the wall.");
            CollectionAssert.Contains(tiles, new Vector3Int(15, 12, 0), $"{pathWalls}: beside the wall's shadow, its line is clear.");
        }
    }

    [TestMethod]
    public void ResolveReachable_APlacedEffectOrAPathThroughWalls_KeepsTheTilesBehindAWall()
    {
        var origin = new Vector3Int(10, 10, 0);
        var lineOfSight = new TargetLineOfSight(new WallTiles((13, 10)));
        var tiles = new List<Vector3Int>();

        foreach (var (path, pathWalls) in new[] { (TargetPath.Placed, PathWallRule.StopsAtWall), (TargetPath.Projectile, PathWallRule.PassesThroughWalls) })
        {
            TargetShapeResolver.ResolveReachable(TargetShape.SingleTarget, TargetAnchor.PathEnd, origin, SingleTile, new TargetRanges(MaximumRange: 6), Bounds, tiles, lineOfSight, path, pathWalls);

            CollectionAssert.Contains(tiles, new Vector3Int(15, 10, 0), $"{path} {pathWalls}: behind the wall.");
        }
    }

    [TestMethod]
    public void Cross_CentredPastTheMapEdge_KeepsOnlyTheArmTilesOnTheMap()
    {
        var bounds = new MapBounds(0, 0, 20, 20, 1);
        var origin = new Vector3Int(18, 5, 0);
        var centre = new Vector3Int(21, 5, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Cross, TargetAnchor.PathEnd, origin, SingleTile, centre, new TargetRanges(MaximumRange: 5, AreaSize: 3), bounds, tiles);

        CollectionAssert.AreEqual(new[] { new Vector3Int(19, 5, 0), new Vector3Int(18, 5, 0) }, tiles);
    }

    [TestMethod]
    public void LineOfSight_DoesNotKeepTheWallsAliveAfterResolving()
    {
        var tiles = new List<Vector3Int>();

        var walls = ResolveWithWallsNobodyKeeps(tiles);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.IsFalse(walls.IsAlive);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference ResolveWithWallsNobodyKeeps(List<Vector3Int> tiles)
    {
        var walls = new WallTiles((12, 10), (10, 13));
        var origin = new Vector3Int(10, 10, 0);
        var lineOfSight = new TargetLineOfSight(walls, LineOfSightRule.Required, LineOfSightRule.Required);
        var largeCaster = new Vector2Byte(3, 3);

        TargetShapeResolver.Resolve(TargetShape.Circle | TargetShape.Adjacent, TargetAnchor.Caster, origin, largeCaster, new Vector3Int(16, 11, 0), new TargetRanges(MaximumRange: 8, AreaSize: 3), Bounds, tiles, lineOfSight);
        TargetShapeResolver.ResolveReachable(TargetShape.Circle, TargetAnchor.PathEnd, origin, largeCaster, new TargetRanges(MaximumRange: 8, AreaSize: 3), Bounds, tiles, lineOfSight);
        return new WeakReference(walls);
    }

    [TestMethod]
    public void Sweep_SingleTileCaster_IsTheThreeTilesItFaces()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Sweep, TargetAnchor.Caster, origin, SingleTile, new Vector3Int(15, 11, 0), new TargetRanges(MaximumRange: 5), Bounds, tiles);

        CollectionAssert.AreEquivalent(new[] { new Vector3Int(11, 9, 0), new Vector3Int(11, 10, 0), new Vector3Int(11, 11, 0) }, tiles);
    }

    [TestMethod]
    public void Sweep_DiagonalAim_IsTheCornerAndTheTwoBesideIt()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Sweep, TargetAnchor.Caster, origin, SingleTile, new Vector3Int(7, 7, 0), new TargetRanges(MaximumRange: 5), Bounds, tiles);

        CollectionAssert.AreEquivalent(new[] { new Vector3Int(9, 9, 0), new Vector3Int(10, 9, 0), new Vector3Int(9, 10, 0) }, tiles);
    }

    [TestMethod]
    public void Sweep_TwoByTwoCaster_IsItsWholeFacingSide()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Sweep, TargetAnchor.Caster, origin, new Vector2Byte(2, 2), new Vector3Int(10, 4, 0), new TargetRanges(MaximumRange: 8), Bounds, tiles);

        CollectionAssert.AreEquivalent(new[] { new Vector3Int(9, 9, 0), new Vector3Int(10, 9, 0), new Vector3Int(11, 9, 0), new Vector3Int(12, 9, 0) }, tiles);
    }

    [TestMethod]
    public void Sweep_AtPathEnd_FacesAwayFromTheCaster()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Sweep, TargetAnchor.PathEnd, origin, SingleTile, new Vector3Int(13, 10, 0), new TargetRanges(MaximumRange: 5), Bounds, tiles);

        CollectionAssert.AreEquivalent(new[] { new Vector3Int(14, 9, 0), new Vector3Int(14, 10, 0), new Vector3Int(14, 11, 0) }, tiles);
    }

    [TestMethod]
    public void NewDirectionalShapes_AreNotAimIndependentAtTheCaster()
    {
        Assert.IsFalse(TargetShapeResolver.IsAimIndependent(TargetShape.Wall, TargetAnchor.Caster));
        Assert.IsFalse(TargetShapeResolver.IsAimIndependent(TargetShape.Sweep, TargetAnchor.Caster));
        Assert.IsTrue(TargetShapeResolver.IsAimIndependent(TargetShape.Ring | TargetShape.Square | TargetShape.Cross, TargetAnchor.Caster));
    }

    [TestMethod]
    public void Cone_EveryDirection_IsEveryTileWithinItsHalfAngleAndLength()
    {
        const int Length = 5;
        var origin = new Vector3Int(20, 20, 0);
        var tiles = new List<Vector3Int>();

        for (var directionX = -7; directionX <= 7; directionX++)
        {
            for (var directionY = -7; directionY <= 7; directionY++)
            {
                if (directionX == 0 && directionY == 0)
                {
                    continue;
                }

                var expected = new List<Vector3Int>();
                for (var offsetY = -Length; offsetY <= Length; offsetY++)
                {
                    for (var offsetX = -Length; offsetX <= Length; offsetX++)
                    {
                        var dot = (long)directionX * offsetX + (long)directionY * offsetY;
                        var directionLengthSquared = (long)directionX * directionX + (long)directionY * directionY;
                        var offsetLengthSquared = (long)offsetX * offsetX + (long)offsetY * offsetY;
                        if ((offsetX != 0 || offsetY != 0) && GridDistance.IsWithinRoundedRadius(offsetX, offsetY, Length) && dot >= 0 && 5 * dot * dot >= 4 * directionLengthSquared * offsetLengthSquared)
                        {
                            expected.Add(new Vector3Int(origin.X + offsetX, origin.Y + offsetY, 0));
                        }
                    }
                }

                TargetShapeResolver.Resolve(TargetShape.Cone, TargetAnchor.Caster, origin, SingleTile, new Vector3Int(origin.X + directionX, origin.Y + directionY, 0), new TargetRanges(MaximumRange: 0, AreaSize: Length), Bounds, tiles);

                CollectionAssert.AreEqual(expected, tiles, $"Aimed along ({directionX}, {directionY}).");
            }
        }
    }

    [TestMethod]
    [DataRow(1, TargetShape.Circle, 6)]
    [DataRow(2, TargetShape.Circle, 2)]
    [DataRow(3, TargetShape.Line, 9)]
    [DataRow(4, TargetShape.Cone, 7)]
    public void ShapeLineOfSight_KeepsExactlyTheTilesTheAnchorSees(int seed, TargetShape shape, int areaSize)
    {
        var random = new Random(seed);
        var origin = new Vector3Int(20, 20, 0);
        var aimedTile = new Vector3Int(24, 22, 0);
        var walls = new WallTiles([.. Enumerable.Range(0, 60).Select(_ => (random.Next(10, 31), random.Next(10, 31))).Where(tile => tile != (20, 20) && tile != (24, 22))]);
        var ranges = new TargetRanges(MaximumRange: 8, AreaSize: areaSize);
        var unfiltered = new List<Vector3Int>();
        var filtered = new List<Vector3Int>();

        TargetShapeResolver.Resolve(shape, TargetAnchor.PathEnd, origin, SingleTile, aimedTile, ranges, Bounds, unfiltered);
        TargetShapeResolver.Resolve(shape, TargetAnchor.PathEnd, origin, SingleTile, aimedTile, ranges, Bounds, filtered, new TargetLineOfSight(walls, Shape: LineOfSightRule.Required));

        CollectionAssert.AreEqual(unfiltered.Where(tile => LineOfSight.IsVisible(aimedTile, tile, walls)).ToList(), filtered);
    }

    [TestMethod]
    public void ResolveReachable_APlacedShapeAroundItsCaster_IsAimedWithinItsArea()
    {
        var origin = new Vector3Int(10, 10, 0);
        var aimable = new List<Vector3Int>();
        var area = new List<Vector3Int>();

        TargetShapeResolver.ResolveReachable(TargetShape.Cone, TargetAnchor.Caster, origin, SingleTile, new TargetRanges(MaximumRange: 0, AreaSize: 4), Bounds, aimable);
        TargetShapeResolver.Resolve(TargetShape.Circle, TargetAnchor.Caster, origin, SingleTile, origin, new TargetRanges(MaximumRange: 0, AreaSize: 4), Bounds, area);

        CollectionAssert.AreEquivalent(area, aimable);
    }

    [TestMethod]
    public void ResolveReachable_AShapeAroundItsCasterWithAPath_IsAimedWithinItsMaximumRange()
    {
        var origin = new Vector3Int(10, 10, 0);
        var aimable = new List<Vector3Int>();

        TargetShapeResolver.ResolveReachable(TargetShape.Cone, TargetAnchor.Caster, origin, SingleTile, new TargetRanges(MaximumRange: 8, AreaSize: 2), Bounds, aimable, path: TargetPath.Projectile);

        CollectionAssert.Contains(aimable, new Vector3Int(18, 10, 0));
    }

    private sealed class WallTiles(params (int X, int Y)[] tiles) : ISightBlockers
    {
        private readonly HashSet<(int X, int Y)> _tiles = [.. tiles];

        public int Version => 0;

        public bool BlocksSight(int x, int y, int z) => _tiles.Contains((x, y));
    }

    [TestMethod]
    public void AimLineOfSight_AnAimBehindAWall_ResolvesNothing()
    {
        var origin = new Vector3Int(10, 10, 0);
        var walls = new WallTiles((12, 10));
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Circle, TargetAnchor.PathEnd, origin, SingleTile, new Vector3Int(14, 10, 0), new TargetRanges(MaximumRange: 8, AreaSize: 1), Bounds, tiles,
            new TargetLineOfSight(walls, Aim: LineOfSightRule.Required));

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void AimLineOfSight_NotRequired_AimsThroughTheWall()
    {
        var origin = new Vector3Int(10, 10, 0);
        var walls = new WallTiles((12, 10));
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, TargetAnchor.PathEnd, origin, SingleTile, new Vector3Int(14, 10, 0), new TargetRanges(MaximumRange: 8), Bounds, tiles,
            new TargetLineOfSight(walls));

        CollectionAssert.AreEqual(new[] { new Vector3Int(14, 10, 0) }, tiles);
    }

    [TestMethod]
    public void AimLineOfSight_Reachable_LeavesOutWhatTheCasterCantSee()
    {
        var origin = new Vector3Int(10, 10, 0);
        var walls = new WallTiles((12, 10));
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.ResolveReachable(TargetShape.SingleTarget, TargetAnchor.PathEnd, origin, SingleTile, new TargetRanges(MaximumRange: 6), Bounds, tiles,
            new TargetLineOfSight(walls, Aim: LineOfSightRule.Required));

        CollectionAssert.DoesNotContain(tiles, new Vector3Int(12, 10, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(11, 10, 0));
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(13, 10, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(13, 12, 0));
        Assert.IsTrue(tiles.All(tile => LineOfSight.IsVisible(origin, tile, walls)));
    }

    [TestMethod]
    public void AimLineOfSight_DoesNotApplyToAShapeTheAimDoesntMove()
    {
        var origin = new Vector3Int(10, 10, 0);
        var walls = new WallTiles((12, 10));
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Adjacent, TargetAnchor.Caster, origin, SingleTile, new Vector3Int(14, 10, 0), new TargetRanges(MaximumRange: 0), Bounds, tiles,
            new TargetLineOfSight(walls, Aim: LineOfSightRule.Required));

        Assert.HasCount(8, tiles);
    }

    [TestMethod]
    public void ShapeLineOfSight_AWallShadowsTheShapesTilesBehindIt()
    {
        var origin = new Vector3Int(10, 10, 0);
        var aimedTile = new Vector3Int(15, 10, 0);
        var walls = new WallTiles((16, 10), (16, 9), (16, 11));
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Circle, TargetAnchor.PathEnd, origin, SingleTile, aimedTile, new TargetRanges(MaximumRange: 8, AreaSize: 3), Bounds, tiles,
            new TargetLineOfSight(walls, Shape: LineOfSightRule.Required));

        CollectionAssert.Contains(tiles, aimedTile);
        CollectionAssert.Contains(tiles, new Vector3Int(16, 10, 0));
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(17, 10, 0));
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(18, 10, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(12, 10, 0));
    }

    [TestMethod]
    public void ShapeLineOfSight_AtCaster_IsSeenFromTheCaster()
    {
        var origin = new Vector3Int(10, 10, 0);
        var walls = new WallTiles((11, 10));
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Circle, TargetAnchor.Caster, origin, SingleTile, origin, new TargetRanges(MaximumRange: 0, AreaSize: 3), Bounds, tiles,
            new TargetLineOfSight(walls, Shape: LineOfSightRule.Required));

        CollectionAssert.Contains(tiles, new Vector3Int(11, 10, 0));
        CollectionAssert.DoesNotContain(tiles, new Vector3Int(12, 10, 0));
        CollectionAssert.Contains(tiles, new Vector3Int(9, 10, 0));
    }

    [TestMethod]
    [DataRow(LineOfSightRule.Required, LineOfSightRule.NotRequired)]
    [DataRow(LineOfSightRule.NotRequired, LineOfSightRule.Required)]
    public void LineOfSight_AnAimInsideAWall_ResolvesNothing(LineOfSightRule aim, LineOfSightRule shape)
    {
        var origin = new Vector3Int(10, 10, 0);
        var wallRow = Enumerable.Range(0, 30).Select(static x => (x, 12)).ToArray();
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Circle, TargetAnchor.PathEnd, origin, SingleTile, new Vector3Int(12, 12, 0), new TargetRanges(MaximumRange: 8, AreaSize: 3), Bounds, tiles,
            new TargetLineOfSight(new WallTiles(wallRow), aim, shape));

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void LineOfSight_Reachable_LeavesOutWallTiles()
    {
        var origin = new Vector3Int(10, 10, 0);
        var wallRow = Enumerable.Range(0, 30).Select(static x => (x, 12)).ToArray();
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.ResolveReachable(TargetShape.Circle, TargetAnchor.PathEnd, origin, SingleTile, new TargetRanges(MaximumRange: 6, AreaSize: 3), Bounds, tiles,
            new TargetLineOfSight(new WallTiles(wallRow), Aim: LineOfSightRule.Required, Shape: LineOfSightRule.Required));

        Assert.IsTrue(tiles.All(static tile => tile.Y < 12));
        CollectionAssert.Contains(tiles, new Vector3Int(12, 11, 0));
    }

    [TestMethod]
    public void NoLineOfSightRule_StillAimsInsideAWall()
    {
        var origin = new Vector3Int(10, 10, 0);
        var tiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.SingleTarget, TargetAnchor.PathEnd, origin, SingleTile, new Vector3Int(12, 12, 0), new TargetRanges(MaximumRange: 8), Bounds, tiles,
            new TargetLineOfSight(new WallTiles((12, 12))));

        CollectionAssert.AreEqual(new[] { new Vector3Int(12, 12, 0) }, tiles);
    }
}
