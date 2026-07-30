namespace Engine.Math;

/// <summary>
/// Resolves a TargetShape into the actual set of map tiles it hits, given where the caster
/// stands and where the cursor currently is. Pure grid math (no IMapQuery/ComponentManager
/// dependency), the same layering reason DistanceFalloff lives here rather than in a Game-layer
/// namespace -- both Game (hit resolution) and Presentation (tile highlighting) call this
/// directly rather than Presentation depending sideways on a Game-layer algorithm.
///
/// Writes into a caller-owned results buffer rather than allocating and returning a new
/// collection -- this is expected to run every frame per armed ability (live hover-tracking
/// recomputes the hit set as the cursor moves, see the Presentation targeting-highlight work),
/// so a fresh List/closure per call would be a permanent, avoidable per-frame GC cost. Callers
/// own one List&lt;Vector3Int&gt; and reuse it call over call; Resolve clears it every call, so
/// the list's capacity stabilizes after the first few frames instead of reallocating.
/// </summary>
public static class TargetShapeResolver
{
    /// <summary>Half-width of a Cone's angular spread, in degrees, on either side of the caster-to-cursor direction. The dot-product test in ResolveCone assumes this stays &lt;= 90 -- see that method's own note.</summary>
    private const double ConeHalfAngleDegrees = 45.0;

    /// <summary>cos^2(ConeHalfAngleDegrees), precomputed once at type load rather than every ResolveCone call (which runs every frame per armed Cone ability) -- ConeHalfAngleDegrees is a compile-time constant, so this never changes at runtime.</summary>
    private static readonly double ConeHalfAngleCosineSquared = Square(System.Math.Cos(ConeHalfAngleDegrees * System.Math.PI / 180.0));

    /// <summary>
    /// Range and areaSize are read differently per shape -- see AbilityTargeting's own doc
    /// comment for the general split. Adjacent ignores both entirely: its footprint is always
    /// exactly the caster's own tile plus its 8 surrounding neighbors, not a per-ability tunable.
    /// </summary>
    public static void Resolve(TargetShape shape, Vector3Int origin, Vector3Int cursorTile, int range, int areaSize, Vector3Int mapSize, List<Vector3Int> results)
    {
        results.Clear();

        switch (shape)
        {
            case TargetShape.Adjacent:
                ResolveAdjacent(origin, mapSize, results);
                break;
            case TargetShape.SingleTarget:
                ResolveSingleTarget(origin, cursorTile, range, results);
                break;
            case TargetShape.Burst:
                ResolveBurst(origin, cursorTile, range, areaSize, mapSize, results);
                break;
            case TargetShape.Line:
                ResolveLine(origin, cursorTile, range, mapSize, results);
                break;
            case TargetShape.Cone:
                ResolveCone(origin, cursorTile, range, mapSize, results);
                break;
        }
    }

    /// <summary>
    /// Burst's cursor-anchored, per-ability-radius diamond scatter. ScatterManhattan always
    /// visits its anchor cell (distance 0) before any neighbor. radius converts to the strength
    /// ScatterManhattan expects via strength = 1 &lt;&lt; radius, since MaxRadius(strength) ==
    /// floor(log2(strength)); the visited-cell falloff magnitude is ignored here -- nothing in
    /// this plan needs distance-based damage falloff yet. Uses the TState overload with a static
    /// lambda (results passed as state) so no closure is allocated per call.
    /// </summary>
    private static void ResolveManhattanBurst(Vector3Int anchor, int radius, Vector3Int mapSize, List<Vector3Int> results)
    {
        if (radius < 0)
        {
            return;
        }

        DistanceFalloff.ScatterManhattan(anchor, 1 << radius, mapSize, results, static (cellPosition, _, resultsList) => resultsList.Add(cellPosition));
    }

    /// <summary>
    /// The caster's own tile plus its 8 surrounding neighbors (Chebyshev distance &lt;= 1) --
    /// melee default. Includes the caster's own tile so a Phasing/Tiny entity sharing it is
    /// still a valid target. A fixed 3x3 block, not the diamond-shaped scatter Burst uses, since
    /// Adjacent's radius is always exactly 1 and diagonals must be included.
    /// </summary>
    private static void ResolveAdjacent(Vector3Int origin, Vector3Int mapSize, List<Vector3Int> results)
    {
        for (var offsetY = -1; offsetY <= 1; offsetY++)
        {
            var cellY = origin.Y + offsetY;
            if (cellY < 0 || cellY >= mapSize.Y)
            {
                continue;
            }

            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                var cellX = origin.X + offsetX;
                if (cellX < 0 || cellX >= mapSize.X)
                {
                    continue;
                }

                results.Add(new Vector3Int(cellX, cellY, origin.Z));
            }
        }
    }

    /// <summary>Exactly cursorTile, valid only when it's within range of the caster -- otherwise no valid target exists at all.</summary>
    private static void ResolveSingleTarget(Vector3Int origin, Vector3Int cursorTile, int range, List<Vector3Int> results)
    {
        if (DistanceFalloff.ManhattanDistance(origin, cursorTile) <= range)
        {
            results.Add(cursorTile);
        }
    }

    /// <summary>Gated by range the same way SingleTarget is -- the cursor tile is where the AOE is centered, not the AOE's own footprint, so it must still be within the caster's reach before the footprint is resolved at all.</summary>
    private static void ResolveBurst(Vector3Int origin, Vector3Int cursorTile, int range, int areaSize, Vector3Int mapSize, List<Vector3Int> results)
    {
        if (DistanceFalloff.ManhattanDistance(origin, cursorTile) > range)
        {
            return;
        }

        ResolveManhattanBurst(cursorTile, areaSize, mapSize, results);
    }

    /// <summary>Steps from origin toward cursorTile along whichever of the 8 cardinal/diagonal directions is nearest the cursor direction, for up to range tiles, stopping early at the map edge.</summary>
    private static void ResolveLine(Vector3Int origin, Vector3Int cursorTile, int range, Vector3Int mapSize, List<Vector3Int> results)
    {
        var direction = NearestEightDirection(origin, cursorTile);
        if (direction.X == 0 && direction.Y == 0)
        {
            return;
        }

        var current = origin;
        for (var step = 0; step < range; step++)
        {
            current = new Vector3Int(current.X + direction.X, current.Y + direction.Y, origin.Z);
            if (current.X < 0 || current.X >= mapSize.X || current.Y < 0 || current.Y >= mapSize.Y)
            {
                break;
            }

            results.Add(current);
        }
    }

    /// <summary>tan(22.5 degrees) and tan(67.5 degrees) -- the octant boundaries a direction's |deltaY|/|deltaX| ratio is compared against below.</summary>
    private const double OctantLowRatio = 0.41421356237; // tan(22.5deg)
    private const double OctantHighRatio = 2.41421356237; // tan(67.5deg)

    /// <summary>
    /// Snaps the origin-to-cursorTile direction to the nearest of the 8 cardinal/diagonal unit
    /// vectors (N/S/E/W/NE/NW/SE/SW), each covering a 45-degree wedge centered on its own axis.
    /// Purely a ratio-of-magnitudes comparison (no trig call needed per resolve, unlike Cone,
    /// since there are only 3 buckets: mostly-horizontal, mostly-vertical, or roughly diagonal).
    /// </summary>
    private static Vector3Int NearestEightDirection(Vector3Int origin, Vector3Int cursorTile)
    {
        var deltaX = cursorTile.X - origin.X;
        var deltaY = cursorTile.Y - origin.Y;

        if (deltaX == 0 && deltaY == 0)
        {
            return new Vector3Int(0, 0, 0);
        }

        var signX = System.Math.Sign(deltaX);
        var signY = System.Math.Sign(deltaY);

        if (deltaX == 0)
        {
            return new Vector3Int(0, signY, 0);
        }

        if (deltaY == 0)
        {
            return new Vector3Int(signX, 0, 0);
        }

        var ratio = (double)System.Math.Abs(deltaY) / System.Math.Abs(deltaX);
        if (ratio < OctantLowRatio)
        {
            return new Vector3Int(signX, 0, 0);
        }

        if (ratio > OctantHighRatio)
        {
            return new Vector3Int(0, signY, 0);
        }

        return new Vector3Int(signX, signY, 0);
    }

    /// <summary>
    /// The one shape that isn't cardinal-only -- a cone needs continuous angle math regardless
    /// of the Manhattan-vs-Chebyshev choice used for adjacency. Two optimizations over a naive
    /// "full square scan, Math.Atan2 per cell" approach, since this runs every frame per armed
    /// Cone ability:
    ///
    /// 1. Per-row X bounds come from the circle directly (maxOffsetXForRow, via one Math.Sqrt
    ///    per row) instead of scanning the full -range..range square and discarding corners --
    ///    the same "bound the loop, don't scan-then-discard" convention ScatterManhattan's own
    ///    diamond bounds already use.
    /// 2. The per-cell angle check is a dot-product/magnitude comparison
    ///    (dot^2 vs cos^2(halfAngle) * |direction|^2 * |offset|^2), not a per-cell Math.Atan2 --
    ///    equivalent to angleBetween(direction, offset) &lt;= halfAngle for any half-angle &lt;= 90
    ///    degrees (cos is monotonically decreasing over [0, 90], and a negative dot product means
    ///    the true angle already exceeds 90, so it's rejected before the squared comparison ever
    ///    needs to distinguish it from a reflex angle). ConeHalfAngleDegrees is 45 today; if a
    ///    future ability ever wants a half-angle &gt; 90, this shortcut needs revisiting.
    /// </summary>
    private static void ResolveCone(Vector3Int origin, Vector3Int cursorTile, int range, Vector3Int mapSize, List<Vector3Int> results)
    {
        var directionDeltaX = cursorTile.X - origin.X;
        var directionDeltaY = cursorTile.Y - origin.Y;
        if (directionDeltaX == 0 && directionDeltaY == 0)
        {
            return;
        }

        var directionLengthSquared = directionDeltaX * directionDeltaX + directionDeltaY * directionDeltaY;
        var rangeSquared = range * range;

        for (var offsetY = -range; offsetY <= range; offsetY++)
        {
            var cellY = origin.Y + offsetY;
            if (cellY < 0 || cellY >= mapSize.Y)
            {
                continue;
            }

            var offsetYSquared = offsetY * offsetY;
            var maxOffsetXForRow = (int)System.Math.Sqrt(System.Math.Max(0, rangeSquared - offsetYSquared));

            for (var offsetX = -maxOffsetXForRow; offsetX <= maxOffsetXForRow; offsetX++)
            {
                if (offsetX == 0 && offsetY == 0)
                {
                    continue;
                }

                var cellX = origin.X + offsetX;
                if (cellX < 0 || cellX >= mapSize.X)
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

                results.Add(new Vector3Int(cellX, cellY, origin.Z));
            }
        }
    }

    private static double Square(double value) => value * value;
}
