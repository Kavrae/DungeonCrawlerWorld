namespace Engine.Math;

/// <summary>
/// Resolves a TargetShape into the actual set of map tiles it hits based on the caster and cursor positions.
/// </summary>
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
/// </remarks>
public static class TargetShapeResolver
{
    /// <summary>Half-width of a Cone's angular spread, in degrees, on either side of the caster-to-cursor direction. The dot-product test in ResolveCone assumes this stays &lt;= 90 -- see that method's own note.</summary>
    private const double ConeHalfAngleDegrees = 45.0;

    /// <summary>cos^2(ConeHalfAngleDegrees), precomputed once at type load rather than every ResolveCone call (which runs every frame per armed Cone ability) -- ConeHalfAngleDegrees is a compile-time constant, so this never changes at runtime.</summary>
    private static readonly double ConeHalfAngleCosineSquared = Square(System.Math.Cos(ConeHalfAngleDegrees * System.Math.PI / 180.0));

    /// <summary>
    /// Fills a list of Vector3Int maptile positions for a given TargetShape.
    /// </summary>
    /// <remarks>
    /// based on the caster's origin and footprint size, the cursor tile, the ability's range and area size, and the map size. 
    /// The results list is cleared at the start of the method.
    /// </remarks>
    public static void Resolve(TargetShape shape, Vector3Int origin, Vector2Byte originSize, Vector3Int cursorTile, int range, int areaSize, Vector3Int mapSize, List<Vector3Int> results)
    {
        results.Clear();

        switch (shape)
        {
            case TargetShape.Adjacent:
                ResolveAdjacent(origin, originSize, mapSize, results);
                break;
            case TargetShape.AdjacentWithSelf:
                ResolveAdjacentWithSelf(origin, originSize, mapSize, results);
                break;
            case TargetShape.SingleTarget:
                ResolveSingleTarget(origin, cursorTile, range, results);
                break;
            case TargetShape.Burst:
                ResolveBurst(origin, cursorTile, range, areaSize, mapSize, results);
                break;
            case TargetShape.Line:
                ResolveLine(origin, originSize, cursorTile, range, mapSize, results);
                break;
            case TargetShape.Cone:
                ResolveCone(origin, originSize, cursorTile, range, mapSize, results);
                break;
            case TargetShape.Self:
                results.Add(origin);
                break;
        }
    }

    /// <summary>
    /// Radius-based diamond scatter.
    /// </summary>
    /// <remarks>
    /// ScatterManhattan always visits its anchor cell (distance 0) before any neighbor.
    /// Radius converts to the strength ScatterManhattan expects via strength = 1 &lt;&lt; radius, since MaxRadius(strength) ==
    /// floor(log2(strength)); 
    /// Rhe visited-cell falloff magnitude is ignored here.
    /// </remarks>
    private static void ResolveManhattanBurst(Vector3Int anchor, int radius, Vector3Int mapSize, List<Vector3Int> results)
    {
        if (radius < 0)
        {
            return;
        }

        DistanceFalloff.ScatterManhattan(anchor, 1 << radius, mapSize, results, static (cellPosition, _, resultsList) => resultsList.Add(cellPosition));
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
    /// ResolveAdjacent's ring plus the caster's own footprint tiles
    /// </summary>
    private static void ResolveAdjacentWithSelf(Vector3Int origin, Vector2Byte originSize, Vector3Int mapSize, List<Vector3Int> results)
    {
        ResolveAdjacent(origin, originSize, mapSize, results);

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

    /// <summary>Exactly cursorTile when it's within range of the caster</summary>
    private static void ResolveSingleTarget(Vector3Int origin, Vector3Int cursorTile, int range, List<Vector3Int> results)
    {
        if (DistanceFalloff.ManhattanDistance(origin, cursorTile) <= range)
        {
            results.Add(cursorTile);
        }
    }

    /// <summary>Manhattan-distance star shape centered on cursorTile</summary>
    private static void ResolveBurst(Vector3Int origin, Vector3Int cursorTile, int range, int areaSize, Vector3Int mapSize, List<Vector3Int> results)
    {
        if (DistanceFalloff.ManhattanDistance(origin, cursorTile) > range)
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
        var rangeSquared = range * range;

        for (var offsetY = -range; offsetY <= range; offsetY++)
        {
            var cellY = effectiveOrigin.Y + offsetY;
            if (cellY < 0 || cellY >= mapSize.Y)
            {
                continue;
            }

            var offsetYSquared = offsetY * offsetY;
            var maxOffsetXForRow = (int)System.Math.Sqrt(System.Math.Max(0, rangeSquared - offsetYSquared));

            for (var offsetX = -maxOffsetXForRow; offsetX <= maxOffsetXForRow; offsetX++)
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

                var offsetLengthSquared = offsetX * offsetX + offsetYSquared;
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
