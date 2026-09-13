namespace Engine.Math;

/// <summary> Resolves a TargetShape into the actual set of map tiles it hits based on the caster and cursor positions. </summary>
/// <remarks>
/// Pure grid math (no IMapQuery/ComponentManager dependency)
///
/// Utilized by both Game (hit resolution) and Presentation (tile highlighting).
///
/// Writes into a caller-owned results buffer rather than allocating and returning a new
/// collection -- this is expected to run every frame per armed ability (live hover-tracking
/// recomputes the hit set as the cursor moves, see the Presentation targeting-highlight work),
/// so a fresh List/closure per call would be a permanent, avoidable per-frame GC cost. Callers
/// own one List&lt;Vector3Int&gt; and reuse it call over call; Resolve clears it every call, so
/// the list's capacity stabilizes after the first few frames instead of reallocating.
///
/// TargetShape is a [Flags] enum -- Resolve unions each set bit's own tiles, with one verified
/// shortcut: Cone/Line/SingleTarget (the three shapes anchored on origin-toward-cursorTile, sharing
/// the same range) nest as SingleTarget &lt;= Line &lt;= Cone for the *same* origin/cursorTile/range,
/// so only the "widest" one present is ever actually resolved -- see ResolveCone's own doc comment
/// for why this requires Cone's extent check to be Chebyshev-based, not Euclidean. This does not
/// extend to Burst (anchored on cursorTile itself, a different geometric family) or Adjacent/Self
/// (never cursor-dependent at all), which always resolve independently. Combined-flag results are
/// de-duplicated only when more than one independently-resolved group actually ran -- the
/// overwhelmingly common single-shape call skips that pass entirely.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class TargetShapeResolver
{
    /// <summary>Half-width of a Cone's angular spread, in degrees, on either side of the caster-to-cursor direction. The dot-product test in ResolveCone assumes this stays &lt;= 90 -- see that method's own note.</summary>
    private const double ConeHalfAngleDegrees = 45.0;

    /// <summary>cos^2(ConeHalfAngleDegrees), precomputed once at type load rather than every ResolveCone call (which runs every frame per armed Cone ability) -- ConeHalfAngleDegrees is a compile-time constant, so this never changes at runtime.</summary>
    private static readonly double ConeHalfAngleCosineSquared = Square(System.Math.Cos(ConeHalfAngleDegrees * System.Math.PI / 180.0));

    /// <summary>Every cursor-anchored, origin-toward-cursor shape (see class doc comment's nesting note) -- tested as one group so Resolve only pays for the widest one present.</summary>
    private const TargetShape CursorDirectedShapes = TargetShape.Cone | TargetShape.Line | TargetShape.SingleTarget;

    /// <summary>Reused across calls to avoid allocating a fresh HashSet for every combined-flag resolve -- see DeduplicateInPlace. [ThreadStatic] defensively, though every caller today runs single-threaded.</summary>
    [ThreadStatic]
    private static HashSet<Vector3Int>? _dedupeScratch;

    /// <summary>
    /// Fills a list of Vector3Int maptile positions for a given TargetShape.
    /// </summary>
    /// <remarks>
    /// based on the caster's origin and footprint size, the cursor tile, the ability's range and area size, and the map size.
    /// The results list is cleared at the start of the method.
    /// </remarks>
    /// <param name="metric">Only consulted by SingleTarget (see TargetingSpec.Metric's own doc comment) -- every other shape has its own fixed distance semantics. Defaults to Manhattan, matching every SingleTarget caller before this parameter existed.</param>
    public static void Resolve(TargetShape shape, Vector3Int origin, Vector2Byte originSize, Vector3Int cursorTile, int range, int areaSize, Vector3Int mapSize, List<Vector3Int> results, DistanceMetric metric = DistanceMetric.Manhattan)
    {
        results.Clear();
        var resolvedGroupCount = 0;

        var cursorDirectedShape = shape & CursorDirectedShapes;
        if (cursorDirectedShape != 0)
        {
            resolvedGroupCount++;
            if ((cursorDirectedShape & TargetShape.Cone) != 0)
            {
                ResolveCone(origin, originSize, cursorTile, range, mapSize, results);
            }
            else if ((cursorDirectedShape & TargetShape.Line) != 0)
            {
                ResolveLine(origin, originSize, cursorTile, range, mapSize, results);
            }
            else
            {
                ResolveSingleTarget(origin, cursorTile, range, metric, results);
            }
        }

        if ((shape & TargetShape.Adjacent) != 0)
        {
            resolvedGroupCount++;
            ResolveAdjacent(origin, originSize, mapSize, results);
        }

        if ((shape & TargetShape.Self) != 0)
        {
            resolvedGroupCount++;
            ResolveSelf(origin, originSize, mapSize, results);
        }

        if ((shape & TargetShape.Burst) != 0)
        {
            resolvedGroupCount++;
            ResolveBurst(origin, cursorTile, range, areaSize, mapSize, results);
        }

        if (resolvedGroupCount > 1)
        {
            DeduplicateInPlace(results);
        }
    }

    /// <summary>Stable in-place de-duplication (first occurrence of each tile wins, relative order preserved) -- only called when Resolve actually combined more than one independently-resolved group.</summary>
    private static void DeduplicateInPlace(List<Vector3Int> results)
    {
        var seen = _dedupeScratch ??= new HashSet<Vector3Int>();
        seen.Clear();

        var writeIndex = 0;
        for (var readIndex = 0; readIndex < results.Count; readIndex++)
        {
            if (seen.Add(results[readIndex]))
            {
                results[writeIndex++] = results[readIndex];
            }
        }

        results.RemoveRange(writeIndex, results.Count - writeIndex);
    }

    /// <summary>
    /// Radius-based diamond scatter.
    /// </summary>
    private static void ResolveManhattanBurst(Vector3Int anchor, int radius, Vector3Int mapSize, List<Vector3Int> results)
    {
        if (radius < 0)
        {
            return;
        }

        DistanceFalloff.ScatterManhattan(anchor, radius, strength: 1, FalloffShape.Flat, mapSize, results, static (cellPosition, _, resultsList) => resultsList.Add(cellPosition));
    }

    /// <summary>The caster's own WxH footprint size, as a single point
    /// </summary>
    /// <remarks>
    /// See ResolveAdjacent's fast path.
    /// </remarks>
    private static readonly Vector2Byte SingleTileFootprint = new(1, 1);

    /// <summary>
    /// The perimeter ring of tiles surrounding the caster's own originSize footprint
    /// </summary>
    /// <remarks>
    /// Chebyshevdistance &lt;= 1 from any footprint cell.
    /// Deliberately excludes every tile of the caster's own footprint, even for a Phasing/Tiny entity sharing one of those tiles
    ///
    /// SingleTileFootprint is run as a common hotpath while larger entities
    /// are given the more generic calculation.
    /// </remarks>
    private static void ResolveAdjacent(Vector3Int origin, Vector2Byte originSize, Vector3Int mapSize, List<Vector3Int> results)
    {
        if (originSize == SingleTileFootprint)
        {
            AddIfOnMap(origin.X - 1, origin.Y - 1, origin.Z, mapSize, results);
            AddIfOnMap(origin.X, origin.Y - 1, origin.Z, mapSize, results);
            AddIfOnMap(origin.X + 1, origin.Y - 1, origin.Z, mapSize, results);
            AddIfOnMap(origin.X - 1, origin.Y, origin.Z, mapSize, results);
            AddIfOnMap(origin.X + 1, origin.Y, origin.Z, mapSize, results);
            AddIfOnMap(origin.X - 1, origin.Y + 1, origin.Z, mapSize, results);
            AddIfOnMap(origin.X, origin.Y + 1, origin.Z, mapSize, results);
            AddIfOnMap(origin.X + 1, origin.Y + 1, origin.Z, mapSize, results);
            return;
        }

        var left = origin.X - 1;
        var right = origin.X + originSize.X;
        var top = origin.Y - 1;
        var bottom = origin.Y + originSize.Y;

        for (var x = left; x <= right; x++)
        {
            AddIfOnMap(x, top, origin.Z, mapSize, results);
            AddIfOnMap(x, bottom, origin.Z, mapSize, results);
        }

        for (var y = origin.Y; y < origin.Y + originSize.Y; y++)
        {
            AddIfOnMap(left, y, origin.Z, mapSize, results);
            AddIfOnMap(right, y, origin.Z, mapSize, results);
        }
    }

    /// <summary>
    /// The caster's own footprint tiles -- combine with Adjacent (Adjacent | Self) for the old
    /// AdjacentWithSelf shape's exact tile set.
    /// </summary>
    private static void ResolveSelf(Vector3Int origin, Vector2Byte originSize, Vector3Int mapSize, List<Vector3Int> results)
    {
        for (var x = origin.X; x < origin.X + originSize.X; x++)
        {
            for (var y = origin.Y; y < origin.Y + originSize.Y; y++)
            {
                AddIfOnMap(x, y, origin.Z, mapSize, results);
            }
        }
    }

    /// <summary>Bounds-checked single-cell add.</summary>
    private static void AddIfOnMap(int x, int y, int z, Vector3Int mapSize, List<Vector3Int> results)
    {
        if (x >= 0 && x < mapSize.X && y >= 0 && y < mapSize.Y)
        {
            results.Add(new Vector3Int(x, y, z));
        }
    }

    /// <summary>
    /// Whether tile falls within originSize's own footprint at origin
    /// </summary>
    private static bool IsWithinFootprint(Vector3Int tile, Vector3Int origin, Vector2Byte originSize) =>
        tile.X >= origin.X && tile.X < origin.X + originSize.X &&
        tile.Y >= origin.Y && tile.Y < origin.Y + originSize.Y;

    /// <summary>
    /// The footprint cell closest to cursorTile
    /// </summary>
    /// <remarks>
    /// She standard closest-point-on-an-axis-aligned-rectangle formula
    /// Clamp the external point onto each axis' footprint range; exact and O(1).
    /// </remarks>
    private static Vector3Int ClosestFootprintCellToCursor(Vector3Int origin, Vector2Byte originSize, Vector3Int cursorTile)
    {
        var closestX = System.Math.Clamp(cursorTile.X, origin.X, origin.X + originSize.X - 1);
        var closestY = System.Math.Clamp(cursorTile.Y, origin.Y, origin.Y + originSize.Y - 1);
        return new Vector3Int(closestX, closestY, origin.Z);
    }

    /// <summary>Exactly cursorTile when it's within range of the caster, under the given distance metric (Manhattan by default -- see TargetingSpec.Metric's own doc comment).</summary>
    private static void ResolveSingleTarget(Vector3Int origin, Vector3Int cursorTile, int range, DistanceMetric metric, List<Vector3Int> results)
    {
        var distance = metric == DistanceMetric.Chebyshev
            ? GridDistance.ChebyshevDistance(origin, cursorTile)
            : GridDistance.ManhattanDistance(origin, cursorTile);
        if (distance <= range)
        {
            results.Add(cursorTile);
        }
    }

    /// <summary>Manhattan-distance star shape centered on cursorTile</summary>
    private static void ResolveBurst(Vector3Int origin, Vector3Int cursorTile, int range, int areaSize, Vector3Int mapSize, List<Vector3Int> results)
    {
        if (GridDistance.ManhattanDistance(origin, cursorTile) > range)
        {
            return;
        }

        ResolveManhattanBurst(cursorTile, areaSize, mapSize, results);
    }

    /// <summary>
    /// A continuous ray from the caster through the cursor
    /// </Summary>
    /// <remarks>
    /// Line of tiles starting from the closest caster tile to the cursor. Extends past the cursor
    /// tile at the same slope until range is reached or the map edge is hit.
    ///
    /// Bresenham's line algorithm.
    ///
    /// Aimable at any point in range.
    /// </remarks>
    private static void ResolveLine(Vector3Int origin, Vector2Byte originSize, Vector3Int cursorTile, int range, Vector3Int mapSize, List<Vector3Int> results)
    {
        var effectiveOrigin = ClosestFootprintCellToCursor(origin, originSize, cursorTile);
        var deltaX = cursorTile.X - effectiveOrigin.X;
        var deltaY = cursorTile.Y - effectiveOrigin.Y;
        if (deltaX == 0 && deltaY == 0)
        {
            return;
        }

        var absDeltaX = System.Math.Abs(deltaX);
        var negativeAbsDeltaY = -System.Math.Abs(deltaY);
        var signX = System.Math.Sign(deltaX);
        var signY = System.Math.Sign(deltaY);
        var error = absDeltaX + negativeAbsDeltaY;

        var x = effectiveOrigin.X;
        var y = effectiveOrigin.Y;

        for (var step = 0; step < range; step++)
        {
            var doubleError = 2 * error;
            if (doubleError >= negativeAbsDeltaY)
            {
                error += negativeAbsDeltaY;
                x += signX;
            }
            if (doubleError <= absDeltaX)
            {
                error += absDeltaX;
                y += signY;
            }

            var current = new Vector3Int(x, y, origin.Z);
            if (current.X < 0 || current.X >= mapSize.X || current.Y < 0 || current.Y >= mapSize.Y)
            {
                break;
            }

            if (IsWithinFootprint(current, origin, originSize))
            {
                continue;
            }

            results.Add(current);
        }
    }

    /// <summary>
    /// Resolves a cone-shaped area of effect
    /// </summary>
    /// <remarks>
    /// The angular sweep is centered on the caster's footprint cell closest to cursorTile.
    ///
    /// Every candidate cell within the caster's own footprint is excluded from the results regardless of angle.
    ///
    /// All cones are currently hard-coded to 90 degrees via ConeHalfAngleDegrees.
    ///
    /// Extent is bounded by Chebyshev distance &lt;= range (a square, not a Euclidean disc) --
    /// deliberately, so that a same-range Line's tiles (each Bresenham step advances Chebyshev
    /// distance from the origin by exactly 1) are always within Cone's own bound too. A Euclidean
    /// bound let a purely diagonal Line's far end (Euclidean distance up to range*sqrt(2)) fall
    /// outside what Cone would return at the identical Range -- see the class doc comment's nesting
    /// note, which this fix is what makes provable.
    /// </remarks>
    private static void ResolveCone(Vector3Int origin, Vector2Byte originSize, Vector3Int cursorTile, int range, Vector3Int mapSize, List<Vector3Int> results)
    {
        var effectiveOrigin = ClosestFootprintCellToCursor(origin, originSize, cursorTile);
        var directionDeltaX = cursorTile.X - effectiveOrigin.X;
        var directionDeltaY = cursorTile.Y - effectiveOrigin.Y;
        if (directionDeltaX == 0 && directionDeltaY == 0)
        {
            return;
        }

        var directionLengthSquared = directionDeltaX * directionDeltaX + directionDeltaY * directionDeltaY;

        for (var offsetY = -range; offsetY <= range; offsetY++)
        {
            var cellY = effectiveOrigin.Y + offsetY;
            if (cellY < 0 || cellY >= mapSize.Y)
            {
                continue;
            }

            for (var offsetX = -range; offsetX <= range; offsetX++)
            {
                var cellX = effectiveOrigin.X + offsetX;
                if (cellX < 0 || cellX >= mapSize.X)
                {
                    continue;
                }

                var candidate = new Vector3Int(cellX, cellY, origin.Z);
                if (IsWithinFootprint(candidate, origin, originSize))
                {
                    continue;
                }

                var dot = directionDeltaX * offsetX + directionDeltaY * offsetY;
                if (dot < 0)
                {
                    continue;
                }

                var offsetLengthSquared = offsetX * offsetX + offsetY * offsetY;
                if ((double)dot * dot < ConeHalfAngleCosineSquared * directionLengthSquared * offsetLengthSquared)
                {
                    continue;
                }

                results.Add(candidate);
            }
        }
    }

    private static double Square(double value) => value * value;
}
