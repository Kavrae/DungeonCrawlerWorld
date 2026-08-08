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
    /// exactly the perimeter ring around the caster's own originSize footprint, not a per-ability
    /// tunable. originSize is otherwise only consulted by Adjacent and Line/Cone (see their own
    /// doc comments) -- Burst/SingleTarget/Self deliberately keep resolving from the single
    /// origin point regardless of the caster's footprint size ("no change for AOE abilities").
    /// </summary>
    public static void Resolve(TargetShape shape, Vector3Int origin, Vector2Byte originSize, Vector3Int cursorTile, int range, int areaSize, Vector3Int mapSize, List<Vector3Int> results)
    {
        results.Clear();

        switch (shape)
        {
            case TargetShape.Adjacent:
                ResolveAdjacent(origin, originSize, mapSize, results);
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

    /// <summary>The caster's own WxH footprint size, as a single point -- see ResolveAdjacent's fast path.</summary>
    private static readonly Vector2Byte SingleTileFootprint = new(1, 1);

    /// <summary>
    /// The perimeter ring of tiles surrounding the caster's own originSize footprint (Chebyshev
    /// distance &lt;= 1 from any footprint cell) -- melee default. Deliberately excludes every
    /// tile of the caster's own footprint, even for a Phasing/Tiny entity sharing one of those
    /// tiles -- an entity hugging the caster's own footprint is meant to be a real, hard-to-deal-
    /// with melee threat, not an automatic target. For a 1x1 caster this is the classic 8
    /// neighbors; for a WxH caster it's 2W + 2H + 4 tiles (e.g. 12 for 2x2, 14 for 2x3).
    ///
    /// Runs every frame for any armed/hovering Adjacent-shaped ability, so the common 1x1 case
    /// (every entity in the game today) takes a fully unrolled fast path with no loop at all --
    /// the same "special-case the 1x1 footprint separately from the general WxH case" precedent
    /// MovementSystem.CanMove already established for this codebase. The general case is four
    /// straight edge scans (top row, bottom row, left column, right column) rather than a
    /// bounding-box loop with a per-cell "is this the caster's own footprint" skip check -- the
    /// latter would visit (W+2)*(H+2) cells and discard W*H of them; four edge scans visit
    /// exactly the 2W + 2H + 4 perimeter cells and nothing else.
    /// </summary>
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

    /// <summary>Bounds-checked single-cell add, shared by ResolveAdjacent's two branches and Line/Cone's own footprint-exclusion pass below.</summary>
    private static void AddIfOnMap(int x, int y, int z, Vector3Int mapSize, List<Vector3Int> results)
    {
        if (x >= 0 && x < mapSize.X && y >= 0 && y < mapSize.Y)
        {
            results.Add(new Vector3Int(x, y, z));
        }
    }

    /// <summary>
    /// Whether tile falls within originSize's own footprint at origin -- used by Line/Cone to
    /// exclude the caster's own tiles from their resolved results (see ResolveLine/ResolveCone's
    /// own doc comments), the same "not my tiles" guarantee Adjacent gets structurally above.
    /// </summary>
    private static bool IsWithinFootprint(Vector3Int tile, Vector3Int origin, Vector2Byte originSize) =>
        tile.X >= origin.X && tile.X < origin.X + originSize.X &&
        tile.Y >= origin.Y && tile.Y < origin.Y + originSize.Y;

    /// <summary>
    /// The footprint cell closest to cursorTile -- the standard closest-point-on-an-axis-aligned-
    /// rectangle formula (clamp the external point onto each axis' footprint range), exact and
    /// O(1). Used as Line/Cone's effective origin for a multi-tile caster, so the aimed line/cone
    /// visibly originates from whichever edge of the caster's footprint is nearest the cursor
    /// rather than always from a single fixed corner.
    /// </summary>
    private static Vector3Int ClosestFootprintCellToCursor(Vector3Int origin, Vector2Byte originSize, Vector3Int cursorTile)
    {
        var closestX = System.Math.Clamp(cursorTile.X, origin.X, origin.X + originSize.X - 1);
        var closestY = System.Math.Clamp(cursorTile.Y, origin.Y, origin.Y + originSize.Y - 1);
        return new Vector3Int(closestX, closestY, origin.Z);
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

    /// <summary>
    /// Steps from the caster's footprint cell closest to cursorTile toward cursorTile along a
    /// continuous ray -- Bresenham's line algorithm (the standard integer-arithmetic "plotLine",
    /// extended past cursorTile at the same slope rather than stopping once it's reached) -- for
    /// up to range tiles, stopping early at the map edge. Aimable at any point in range, not
    /// snapped to one of 8 buckets the way this used to work -- two cursor tiles that would
    /// previously have collapsed onto the same "mostly horizontal" (or vertical/diagonal) bucket
    /// now trace genuinely different lines, the same "any angle" freedom Cone already has (see
    /// ResolveCone's own doc comment). No floating-point/trig involved -- Bresenham decides each
    /// step's direction from an integer error accumulator, exactly one new grid cell per range
    /// step, same contract the old 8-direction stepper had. For a 1x1 caster the closest
    /// footprint cell is always origin itself (unchanged from before multi-tile support existed).
    /// Explicitly strips out any resulting tile that falls back within the caster's own
    /// footprint -- never happens for a 1x1 caster (a line steps away from its own origin, never
    /// back onto it), but a large-enough footprint stepping from one corner could otherwise clip
    /// back across another part of the same rectangle.
    /// </summary>
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
    ///
    /// The angular sweep is centered on the caster's footprint cell closest to cursorTile (see
    /// ClosestFootprintCellToCursor), same as Line, and every candidate cell within the caster's
    /// own footprint is excluded from the results regardless of angle -- for a 1x1 caster this
    /// is exactly the old "skip offset (0,0)" special case; for a multi-tile caster it's a real
    /// membership check, since the swept circle can extend past the effective-origin corner and
    /// clip back across another part of the same footprint rectangle.
    /// </summary>
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
