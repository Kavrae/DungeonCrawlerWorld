using Engine.Math;

namespace Engine.Tiles;

/// <summary> Resolves a TargetShape at its TargetAnchor into the actual set of map tiles it hits based on the caster and the aimed tile. </summary>
/// <remarks>
/// Pure grid math (no IMapQuery/ComponentManager dependency)
///
/// Utilized by both Game (hit resolution) and Presentation (tile highlighting).
///
/// Every range and area is one measure: a rounded circle (GridDistance.IsWithinRoundedRadius), with the ranges measured from
/// the caster's nearest footprint tile and the area from the anchor.
///
/// At TargetAnchor.PathEnd the aimed tile must lie in the range band (TargetRanges.IsInRangeBand), or nothing resolves. At
/// TargetAnchor.Caster the aim only gives a direction, which Resolve never range-checks (where it may be aimed is AimRanges);
/// the caster's footprint is left out of every shape but Self, and so is every tile nearer than the minimum range.
///
/// Writes into a caller-owned results buffer rather than allocating and returning a new
/// collection -- this is expected to run every frame per armed ability (live hover-tracking
/// recomputes the hit set as the cursor moves, see the Presentation targeting-highlight work),
/// so a fresh List/closure per call would be a permanent, avoidable per-frame GC cost. Callers
/// own one List&lt;Vector3Int&gt; and reuse it call over call; Resolve clears it every call, so
/// the list's capacity stabilizes after the first few frames instead of reallocating.
///
/// TargetShape is a [Flags] enum -- Resolve unions each set bit's own tiles, with one verified
/// shortcut at TargetAnchor.Caster: Line nests inside Cone for the same origin, aim and length, so only the wider one present
/// is resolved. They nest because both stop at the same rounded circle around the footprint tile nearest the aim, a Line's
/// tiles move away from it steadily along the aim, and a Line's tiles stray from the aim by less than half a tile, which keeps
/// even its first step strictly inside Cone's half-angle of arctan(1/2). Combined-flag results are de-duplicated only when
/// more than one independently-resolved group actually ran -- the overwhelmingly common single-shape call skips that pass
/// entirely.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class TargetShapeResolver
{
    /// <summary>The shapes that face from the caster toward the aimed tile.</summary>
    private const TargetShape DirectionalShapes = TargetShape.Cone | TargetShape.Line | TargetShape.Wall | TargetShape.Sweep;

    /// <summary>The caster's own WxH footprint size, as a single point</summary>
    /// <remarks>See ResolveAdjacent's fast path, and every shape anchored on the one end tile.</remarks>
    private static readonly Vector2Byte SingleTileFootprint = new(1, 1);

    /// <summary>Reused across calls by DeduplicateInPlace. [ThreadStatic] defensively, though every caller today runs single-threaded.</summary>
    [ThreadStatic]
    private static TileMarkGrid? _dedupeScratch;

    /// <summary>Whether shape at anchor resolves to the same tiles wherever it is aimed: anchored on the caster and facing no direction.</summary>
    public static bool IsAimIndependent(TargetShape shape, TargetAnchor anchor) =>
        anchor == TargetAnchor.Caster && !IsDirectional(shape);

    /// <summary>Whether shape holds a shape that faces from the caster toward the aimed tile: Line, Cone, Wall or Sweep.</summary>
    public static bool IsDirectional(TargetShape shape) => (shape & DirectionalShapes) != 0;

    /// <summary>The ranges the aimed tile is held to: ranges, except that a shape anchored on the caster with no path is aimed only for a direction, within its own reach -- its AreaSize, at least 1 -- whatever MaximumRange says.</summary>
    public static TargetRanges AimRanges(TargetAnchor anchor, TargetPath path, TargetRanges ranges) =>
        anchor == TargetAnchor.Caster && path == TargetPath.Placed ? ranges with { MaximumRange = System.Math.Max(ranges.AreaSize, 1) } : ranges;

    /// <summary>
    /// Fills a list of Vector3Int maptile positions for a given TargetShape at anchor.
    /// </summary>
    /// <remarks>
    /// based on the caster's origin and footprint size, the aimed tile, the ranges, and the map's bounds.
    /// The results list is cleared at the start of the method.
    ///
    /// With lineOfSight's aim rule, an aim the caster's footprint can't see (LineOfSight) resolves nothing, unless the shape
    /// is the same wherever it is aimed (IsAimIndependent). With its shape rule, only the tiles visible from the anchor are
    /// kept: from the end tile at TargetAnchor.PathEnd, from any tile of the caster's footprint at TargetAnchor.Caster.
    /// With either rule, an end tile inside a wall resolves nothing at TargetAnchor.PathEnd (TargetLineOfSight.PlacementWalls): a
    /// wall never hides its own tile, so a shape centred inside one would see out of both sides of it.
    /// </remarks>
    public static void Resolve(TargetShape shape, TargetAnchor anchor, Vector3Int origin, Vector2Byte originSize, Vector3Int aimedTile, TargetRanges ranges, MapBounds bounds, List<Vector3Int> results, TargetLineOfSight lineOfSight = default) =>
        Resolve(shape, anchor, origin, originSize, aimedTile, endTile: aimedTile, ranges, bounds, results, lineOfSight);

    /// <summary>Resolve, for a shape whose path ended at endTile rather than on the aimed tile: a projectile that stopped short, or flew on or bounced.</summary>
    /// <remarks>
    /// The aim checks (range band, line of sight) and the direction directional shapes face are still the aimed tile's; at
    /// TargetAnchor.PathEnd the shape is laid out on endTile, which a wall must not hold and the shape rule sees from.
    /// </remarks>
    public static void Resolve(TargetShape shape, TargetAnchor anchor, Vector3Int origin, Vector2Byte originSize, Vector3Int aimedTile, Vector3Int endTile, TargetRanges ranges, MapBounds bounds, List<Vector3Int> results, TargetLineOfSight lineOfSight = default) =>
        ResolveAt(shape, anchor, origin, originSize, aimedTile, endTile, ranges, bounds, results, lineOfSight, checksAim: true);

    /// <summary>Traces request's path over lineOfSight's walls (PathTracer) and fills results with shape's tiles where it ended, as Resolve does for an endTile: false, with nothing traced or resolved, when the aim isn't allowed.</summary>
    /// <remarks>
    /// The aim is allowed when, for a shape at TargetAnchor.PathEnd, it lies in the range band, and under lineOfSight's aim
    /// rule the caster's footprint sees it -- whatever the shape, since the path is fired there. The line the aim is seen
    /// along is the line a path that walls turn or stop follows, so it is searched for once. pathTiles, metEntities and
    /// results are cleared first.
    /// </remarks>
    public static bool TryResolveAlongPath(TargetShape shape, TargetAnchor anchor, in TargetPathRequest request, MapBounds bounds, IPathOccupants? occupants, List<Vector3Int> pathTiles, List<int> metEntities, List<Vector3Int> results, out PathTrace trace, TargetLineOfSight lineOfSight = default)
    {
        results.Clear();
        pathTiles.Clear();
        metEntities.Clear();
        trace = default;
        var origin = request.Origin;
        var originSize = request.OriginSize;
        if (anchor == TargetAnchor.PathEnd && !request.Ranges.IsInRangeBand(origin, originSize, request.AimedTile))
        {
            return false;
        }

        DigitalLine? sharedLine = null;
        if (lineOfSight.AimWalls is { } aimWalls)
        {
            var aimTile = new Vector3Int(request.AimedTile.X, request.AimedTile.Y, origin.Z);
            if (!LineOfSight.FindLineFromFootprint(origin, originSize, aimTile, aimWalls, out var aimLine))
            {
                return false;
            }

            sharedLine = PathTracer.FollowsSightPastWalls(in request) ? aimLine : null;
        }

        var tracer = sharedLine is { } line
            ? new PathTracer(in request, lineOfSight.Walls, bounds, line)
            : new PathTracer(in request, lineOfSight.Walls, bounds);
        trace = tracer.RunToEnd(occupants, pathTiles, metEntities);
        ResolveAt(shape, anchor, origin, originSize, request.AimedTile, trace.EndTile, request.Ranges, bounds, results, lineOfSight, checksAim: false);
        return true;
    }

    /// <summary>Resolve's endTile overload, checking the aim's line of sight only when checksAim (TryResolveAlongPath has already).</summary>
    private static void ResolveAt(TargetShape shape, TargetAnchor anchor, Vector3Int origin, Vector2Byte originSize, Vector3Int aimedTile, Vector3Int endTile, TargetRanges ranges, MapBounds bounds, List<Vector3Int> results, TargetLineOfSight lineOfSight, bool checksAim)
    {
        results.Clear();
        var anchorOrigin = anchor == TargetAnchor.Caster ? origin : endTile;
        var anchorSize = anchor == TargetAnchor.Caster ? originSize : SingleTileFootprint;
        var shapeWalls = lineOfSight.ShapeWalls is { } walls ? SightBlockerCache.Around(walls, anchorOrigin, anchorSize, ranges.AreaSize) : null;
        try
        {
            if (checksAim && lineOfSight.AimWalls is { } aimWalls && !IsAimIndependent(shape, anchor) && !LineOfSight.IsVisibleFromFootprint(origin, originSize, aimedTile, shapeWalls ?? aimWalls))
            {
                return;
            }

            if (anchor == TargetAnchor.PathEnd && lineOfSight.PlacementWalls is { } placementWalls && (shapeWalls ?? placementWalls).BlocksSight(endTile.X, endTile.Y, endTile.Z))
            {
                return;
            }

            var resolvedGroupCount = anchor == TargetAnchor.Caster
                ? AppendAtCaster(shape, origin, originSize, aimedTile, ranges, bounds, results)
                : AppendAtPathEnd(shape, origin, originSize, aimedTile, endTile, ranges, bounds, results);

            if (resolvedGroupCount > 1)
            {
                DeduplicateInPlace(results);
            }

            if (shapeWalls is not null)
            {
                RemoveHiddenFrom(anchorOrigin, anchorSize, shapeWalls, firstIndex: 0, results);
            }
        }
        finally
        {
            shapeWalls?.Release();
        }
    }

    /// <summary>
    /// Fills results with every tile the given shape can be aimed at or reach from origin, whatever the aim -- the area
    /// a player picks from before there is a direction.
    /// </summary>
    /// <remarks>
    /// A shape that is aim-independent (IsAimIndependent) is its own tiles. Otherwise it is the range band, MinimumRange to
    /// MaximumRange of the footprint -- with the footprint itself excluded at TargetAnchor.Caster, where aiming at it gives no
    /// direction, and with lineOfSight's aim rule only the tiles the footprint can see (LineOfSight.ComputeVisible), and at TargetAnchor.PathEnd under either rule no wall tile -- plus,
    /// at TargetAnchor.Caster, the tiles of whatever non-directional shapes are combined with it. The results list is
    /// cleared at the start of the method.
    ///
    /// The band is AimRanges': for a shape anchored on the caster with no path, its own reach.
    ///
    /// When the aim fires a path (path not Placed) that lineOfSight's walls stop or turn (pathWalls StopsAtWall or
    /// BouncesOffWalls), only the band tiles the path's first leg reaches are kept: since a path follows the line sight sees
    /// its aim along (PathTracer), those are the tiles the footprint can see that aren't walls. A path that passes through
    /// walls, or none, keeps the whole band.
    /// </remarks>
    public static void ResolveReachable(TargetShape shape, TargetAnchor anchor, Vector3Int origin, Vector2Byte originSize, TargetRanges ranges, MapBounds bounds, List<Vector3Int> results, TargetLineOfSight lineOfSight = default, TargetPath path = TargetPath.Placed, PathWallRule pathWalls = PathWallRule.StopsAtWall)
    {
        if (IsAimIndependent(shape, anchor))
        {
            Resolve(shape, anchor, origin, originSize, origin, ranges, bounds, results, lineOfSight);
            return;
        }

        var aimRanges = AimRanges(anchor, path, ranges);
        results.Clear();
        AppendRangeBand(origin, originSize, aimRanges, excludesFootprint: anchor == TargetAnchor.Caster, bounds, results);

        var casterCentredShape = anchor == TargetAnchor.Caster ? shape & ~DirectionalShapes : TargetShape.None;
        var firesIntoWalls = path != TargetPath.Placed && pathWalls is PathWallRule.StopsAtWall or PathWallRule.BouncesOffWalls && lineOfSight.Walls is not null;
        var sightWalls = lineOfSight.AimWalls ?? (firesIntoWalls ? lineOfSight.Walls : null);
        var removesWalls = (anchor == TargetAnchor.PathEnd && lineOfSight.PlacementWalls is not null) || firesIntoWalls;
        var hidesCasterCentred = casterCentredShape != TargetShape.None && lineOfSight.ShapeWalls is not null;
        var sweepsLargeFootprint = sightWalls is not null && originSize != SingleTileFootprint;
        var cachedWalls = lineOfSight.Walls is { } walls && (removesWalls || hidesCasterCentred || sweepsLargeFootprint)
            ? SightBlockerCache.Around(walls, origin, originSize, System.Math.Max(aimRanges.MaximumRange, ranges.AreaSize))
            : null;
        try
        {
            var sweepRadius = aimRanges.MaximumRange + System.Math.Max(originSize.X, originSize.Y) - 1;
            var visible = sightWalls is not null ? VisibleFromFootprint(origin, originSize, sweepRadius, cachedWalls ?? sightWalls) : null;
            if (visible is not null || removesWalls)
            {
                KeepAimableTiles(visible, removesWalls ? cachedWalls : null, firstIndex: 0, results);
            }

            visible?.Release();
            if (casterCentredShape != TargetShape.None)
            {
                var firstCasterCentred = results.Count;
                AppendAtCaster(casterCentredShape, origin, originSize, origin, ranges, bounds, results);
                if (hidesCasterCentred)
                {
                    RemoveHiddenFrom(origin, originSize, cachedWalls!, firstCasterCentred, results);
                }

                DeduplicateInPlace(results);
            }
        }
        finally
        {
            cachedWalls?.Release();
        }
    }

    /// <summary>Reused across calls by VisibleFromFootprint. [ThreadStatic] defensively, like _dedupeScratch.</summary>
    [ThreadStatic]
    private static TileMarkGrid? _visibleScratch;

    /// <summary>Every tile within Chebyshev distance radius of the footprint at origin that some footprint tile can see, in a reused grid the caller releases.</summary>
    /// <remarks>Each footprint tile is swept, since an inner one can see past walls the edge ones can't; pass a SightBlockerCache as walls so a larger footprint's sweeps ask about each wall once.</remarks>
    private static TileMarkGrid VisibleFromFootprint(Vector3Int origin, Vector2Byte originSize, int radius, ISightBlockers walls)
    {
        var extent = System.Math.Max(radius, 0);
        var visible = (_visibleScratch ??= new TileMarkGrid())
            .Reset(origin.X - extent, origin.Y - extent, originSize.X + 2 * extent, originSize.Y + 2 * extent);
        var sink = new TileMarkGrid.Sink(visible);
        for (var x = origin.X; x < origin.X + originSize.X; x++)
        {
            for (var y = origin.Y; y < origin.Y + originSize.Y; y++)
            {
                LineOfSight.ComputeVisible(new Vector3Int(x, y, origin.Z), radius, walls, ref sink);
            }
        }

        return visible;
    }

    /// <summary>Keeps, in order from firstIndex on, the tiles in visible (when given) that walls (when given) doesn't block.</summary>
    private static void KeepAimableTiles(TileMarkGrid? visible, ISightBlockers? walls, int firstIndex, List<Vector3Int> results)
    {
        var writeIndex = firstIndex;
        for (var readIndex = firstIndex; readIndex < results.Count; readIndex++)
        {
            var tile = results[readIndex];
            if ((visible is null || visible.Contains(tile)) && (walls is null || !walls.BlocksSight(tile.X, tile.Y, tile.Z)))
            {
                results[writeIndex++] = tile;
            }
        }

        results.RemoveRange(writeIndex, results.Count - writeIndex);
    }

    /// <summary>What sweeping one tile costs, in steps of a ray: measured, so a Circle past radius 3 or a filled Square is swept and a Line, a Cone or a small Circle is tested ray by ray.</summary>
    private const long SweptTileCostInRaySteps = 3;

    /// <summary>Removes, from firstIndex on, every tile no tile of the footprint at origin can see, keeping the rest in order.</summary>
    /// <remarks>
    /// A shape that fills its area (a Circle, a Square) is swept once (VisibleFromFootprint), at a cost that grows with the
    /// square around it; a thin one (a Line, a Wall) tests each tile's own ray, at a cost that grows with its tiles times
    /// their distance. The two always agree (LineOfSight). Pass a SightBlockerCache as walls, so a wall near the footprint
    /// that many rays or sweeps pass is asked about once.
    /// </remarks>
    private static void RemoveHiddenFrom(Vector3Int origin, Vector2Byte originSize, ISightBlockers walls, int firstIndex, List<Vector3Int> results)
    {
        var farthest = 0;
        for (var index = firstIndex; index < results.Count; index++)
        {
            farthest = System.Math.Max(farthest, GridDistance.ChebyshevDistance(origin, originSize, results[index], SingleTileFootprint));
        }

        var sweepRadius = farthest + System.Math.Max(originSize.X, originSize.Y) - 1;
        var sweptSide = 2L * sweepRadius + System.Math.Max(originSize.X, originSize.Y);
        if ((long)(results.Count - firstIndex) * (farthest + 1) >= SweptTileCostInRaySteps * sweptSide * sweptSide)
        {
            var visible = VisibleFromFootprint(origin, originSize, sweepRadius, walls);
            KeepAimableTiles(visible, walls: null, firstIndex, results);
            visible.Release();
            return;
        }

        var writeIndex = firstIndex;
        for (var readIndex = firstIndex; readIndex < results.Count; readIndex++)
        {
            if (LineOfSight.IsVisibleFromFootprint(origin, originSize, results[readIndex], walls))
            {
                results[writeIndex++] = results[readIndex];
            }
        }

        results.RemoveRange(writeIndex, results.Count - writeIndex);
    }

    /// <summary>
    /// target, or -- when it lies outside the range band -- the tile in the band nearest it on the line from the caster's
    /// footprint tile nearest it: the farthest in range on the way to a target past MaximumRange, the first far enough past
    /// a target nearer than MinimumRange.
    /// </summary>
    /// <remarks>target itself when it is on the caster's footprint (no direction) or no tile on that line is in the band.</remarks>
    public static Vector3Int ClampToRangeBand(Vector3Int origin, Vector2Byte originSize, Vector3Int target, TargetRanges ranges)
    {
        if (ranges.IsInRangeBand(origin, originSize, target))
        {
            return target;
        }

        var start = ClosestFootprintCellTo(origin, originSize, target);
        if (start.X == target.X && start.Y == target.Y)
        {
            return target;
        }

        var stepper = new GridLineStepper(start, target);
        if (ranges.IsNearerThanMinimum(origin, originSize, target))
        {
            while (true)
            {
                var beyond = stepper.Next();
                if (!GridDistance.IsWithinRoundedRadius(origin, originSize, beyond, ranges.MaximumRange))
                {
                    return target;
                }

                if (!ranges.IsNearerThanMinimum(origin, originSize, beyond))
                {
                    return beyond;
                }
            }
        }

        var clamped = ranges.IsInRangeBand(origin, originSize, start) ? start : target;
        while (true)
        {
            var step = stepper.Next();
            if ((step.X == target.X && step.Y == target.Y) || !GridDistance.IsWithinRoundedRadius(origin, originSize, step, ranges.MaximumRange))
            {
                return clamped;
            }

            if (!ranges.IsNearerThanMinimum(origin, originSize, step))
            {
                clamped = step;
            }
        }
    }

    /// <summary>Appends shape's tiles around the caster's footprint, leaving out the footprint (except for Self) and every tile nearer than MinimumRange; returns how many groups were resolved.</summary>
    private static int AppendAtCaster(TargetShape shape, Vector3Int origin, Vector2Byte originSize, Vector3Int aimedTile, TargetRanges ranges, MapBounds bounds, List<Vector3Int> results)
    {
        var firstAppended = results.Count;
        var resolvedGroupCount = 0;
        var nearestFootprintTile = ClosestFootprintCellTo(origin, originSize, aimedTile);
        var directionX = aimedTile.X - nearestFootprintTile.X;
        var directionY = aimedTile.Y - nearestFootprintTile.Y;

        if ((shape & (TargetShape.Cone | TargetShape.Line)) != 0)
        {
            resolvedGroupCount++;
            if ((shape & TargetShape.Cone) != 0)
            {
                AppendCone(nearestFootprintTile, directionX, directionY, ranges.AreaSize, origin, originSize, excludesFootprint: true, bounds, results);
            }
            else
            {
                AppendLine(nearestFootprintTile, aimedTile, nearestFootprintTile, ranges.AreaSize, origin, originSize, bounds, results);
            }
        }

        resolvedGroupCount += AppendDirectionless(shape & ~TargetShape.Self, origin, originSize, ranges.AreaSize, bounds, results);
        resolvedGroupCount += AppendFacing(shape, origin, originSize, nearestFootprintTile, directionX, directionY, ranges.AreaSize, bounds, results);
        RemoveFootprint(origin, originSize, firstAppended, results);

        if ((shape & TargetShape.Self) != 0)
        {
            resolvedGroupCount++;
            AppendFootprint(origin, originSize, bounds, results);
        }

        if (ranges.MinimumRange > 0)
        {
            RemoveNearerThanMinimum(origin, originSize, ranges, firstAppended, results);
        }

        return resolvedGroupCount;
    }

    /// <summary>Appends shape's tiles at endTile, where the path finished, when the aimed tile lies in the range band; returns how many groups were resolved.</summary>
    /// <remarks>Self is the caster's footprint, so it adds nothing here. Directional shapes face from the caster toward the aimed tile. A Line carries on along the caster-to-aim line when endTile is the aimed tile, else starts afresh from endTile in that direction.</remarks>
    private static int AppendAtPathEnd(TargetShape shape, Vector3Int origin, Vector2Byte originSize, Vector3Int aimedTile, Vector3Int endTile, TargetRanges ranges, MapBounds bounds, List<Vector3Int> results)
    {
        if (!ranges.IsInRangeBand(origin, originSize, aimedTile))
        {
            return 0;
        }

        var resolvedGroupCount = 0;

        if ((shape & TargetShape.SingleTarget) != 0)
        {
            resolvedGroupCount++;
            AddIfOnMap(endTile.X, endTile.Y, endTile.Z, bounds, results);
        }

        resolvedGroupCount += AppendDirectionless(shape, endTile, SingleTileFootprint, ranges.AreaSize, bounds, results);

        var start = ClosestFootprintCellTo(origin, originSize, aimedTile);
        var directionX = aimedTile.X - start.X;
        var directionY = aimedTile.Y - start.Y;

        if ((shape & TargetShape.Line) != 0)
        {
            resolvedGroupCount++;
            if (endTile == aimedTile)
            {
                AppendLine(start, aimedTile, aimedTile, ranges.AreaSize, origin, originSize, bounds, results);
            }
            else if (directionX != 0 || directionY != 0)
            {
                AppendLine(endTile, new Vector3Int(endTile.X + directionX, endTile.Y + directionY, endTile.Z), endTile, ranges.AreaSize, origin, originSize, bounds, results);
            }
        }

        if ((shape & TargetShape.Cone) != 0)
        {
            resolvedGroupCount++;
            AppendCone(endTile, directionX, directionY, ranges.AreaSize, origin, originSize, excludesFootprint: false, bounds, results);
        }

        resolvedGroupCount += AppendFacing(shape, endTile, SingleTileFootprint, endTile, directionX, directionY, ranges.AreaSize, bounds, results);
        return resolvedGroupCount;
    }

    /// <summary>Appends the tiles of shape's direction-free bits other than Self and SingleTarget (Adjacent, Circle, Ring, Square, Cross) around the footprint at anchor; returns how many groups were resolved.</summary>
    private static int AppendDirectionless(TargetShape shape, Vector3Int anchor, Vector2Byte anchorSize, int areaSize, MapBounds bounds, List<Vector3Int> results)
    {
        var resolvedGroupCount = 0;

        if ((shape & TargetShape.Adjacent) != 0)
        {
            resolvedGroupCount++;
            AppendAdjacent(anchor, anchorSize, bounds, results);
        }

        if ((shape & TargetShape.Circle) != 0)
        {
            resolvedGroupCount++;
            AppendRoundedDisc(anchor, anchorSize, areaSize, excludesFootprint: false, bounds, results);
        }

        if ((shape & TargetShape.Ring) != 0)
        {
            resolvedGroupCount++;
            AppendRing(anchor, anchorSize, areaSize, bounds, results);
        }

        if ((shape & TargetShape.Square) != 0)
        {
            resolvedGroupCount++;
            AppendSquare(anchor, anchorSize, areaSize, bounds, results);
        }

        if ((shape & TargetShape.Cross) != 0)
        {
            resolvedGroupCount++;
            AppendCross(anchor, anchorSize, areaSize, bounds, results);
        }

        return resolvedGroupCount;
    }

    /// <summary>Appends the tiles of shape's Wall and Sweep bits, facing (directionX, directionY): a Wall across it centred on wallCentre, a Sweep on the facing side of the footprint at anchor; returns how many groups were resolved.</summary>
    private static int AppendFacing(TargetShape shape, Vector3Int anchor, Vector2Byte anchorSize, Vector3Int wallCentre, int directionX, int directionY, int areaSize, MapBounds bounds, List<Vector3Int> results)
    {
        var resolvedGroupCount = 0;

        if ((shape & TargetShape.Wall) != 0)
        {
            resolvedGroupCount++;
            AppendWall(wallCentre, directionX, directionY, areaSize, bounds, results);
        }

        if ((shape & TargetShape.Sweep) != 0)
        {
            resolvedGroupCount++;
            AppendSweep(anchor, anchorSize, directionX, directionY, bounds, results);
        }

        return resolvedGroupCount;
    }

    /// <summary>The band one tile thick at the edge of a rounded circle of radius around the footprint at anchor: within radius, not within radius - 1.</summary>
    private static void AppendRing(Vector3Int anchor, Vector2Byte anchorSize, int radius, MapBounds bounds, List<Vector3Int> results) =>
        AppendRoundedBand(anchor, anchorSize, radius, innerRadius: radius - 1, excludesFootprint: false, bounds, results);

    /// <summary>Every tile within Chebyshev distance halfSide of the footprint at anchor.</summary>
    private static void AppendSquare(Vector3Int anchor, Vector2Byte anchorSize, int halfSide, MapBounds bounds, List<Vector3Int> results)
    {
        if (halfSide < 0)
        {
            return;
        }

        var firstY = System.Math.Max(anchor.Y - halfSide, bounds.MinY);
        var endY = System.Math.Min(anchor.Y + anchorSize.Y + halfSide, bounds.MaxY);
        var firstX = System.Math.Max(anchor.X - halfSide, bounds.MinX);
        var endX = System.Math.Min(anchor.X + anchorSize.X + halfSide, bounds.MaxX);
        for (var y = firstY; y < endY; y++)
        {
            AddRow(firstX, endX - 1, y, anchor.Z, results);
        }
    }

    /// <summary>The footprint at anchor and the tiles in its own columns and rows out to armLength from it.</summary>
    private static void AppendCross(Vector3Int anchor, Vector2Byte anchorSize, int armLength, MapBounds bounds, List<Vector3Int> results)
    {
        if (armLength < 0)
        {
            return;
        }

        var firstColumnY = System.Math.Max(anchor.Y - armLength, bounds.MinY);
        var endColumnY = System.Math.Min(anchor.Y + anchorSize.Y + armLength, bounds.MaxY);
        for (var x = System.Math.Max(anchor.X, bounds.MinX); x < System.Math.Min(anchor.X + anchorSize.X, bounds.MaxX); x++)
        {
            for (var y = firstColumnY; y < endColumnY; y++)
            {
                results.Add(new Vector3Int(x, y, anchor.Z));
            }
        }

        var firstLeftArm = (int)System.Math.Max(1, (long)anchor.X - bounds.MaxX + 1);
        var lastLeftArm = (int)System.Math.Min(armLength, (long)anchor.X - bounds.MinX);
        var firstRightArm = (int)System.Math.Max(1, (long)bounds.MinX - anchor.X - anchorSize.X + 1);
        var lastRightArm = (int)System.Math.Min(armLength, (long)bounds.MaxX - anchor.X - anchorSize.X);
        var firstArm = System.Math.Min(firstLeftArm, firstRightArm);
        var lastArm = System.Math.Max(lastLeftArm, lastRightArm);
        for (var y = System.Math.Max(anchor.Y, bounds.MinY); y < System.Math.Min(anchor.Y + anchorSize.Y, bounds.MaxY); y++)
        {
            for (var arm = firstArm; arm <= lastArm; arm++)
            {
                if (arm >= firstLeftArm && arm <= lastLeftArm)
                {
                    results.Add(new Vector3Int(anchor.X - arm, y, anchor.Z));
                }

                if (arm >= firstRightArm && arm <= lastRightArm)
                {
                    results.Add(new Vector3Int(anchor.X + anchorSize.X - 1 + arm, y, anchor.Z));
                }
            }
        }
    }

    /// <summary>A line of length tiles across (directionX, directionY), centred on centre: length / 2 tiles to one side, (length - 1) / 2 to the other. Nothing for a zero direction.</summary>
    /// <remarks>Each side is Bresenham's line along the perpendicular, so across a diagonal it is a line of tiles touching at corners.</remarks>
    private static void AppendWall(Vector3Int centre, int directionX, int directionY, int length, MapBounds bounds, List<Vector3Int> results)
    {
        if ((directionX == 0 && directionY == 0) || length <= 0)
        {
            return;
        }

        AddIfOnMap(centre.X, centre.Y, centre.Z, bounds, results);
        AppendWallSide(centre, -directionY, directionX, length / 2, bounds, results);
        AppendWallSide(centre, directionY, -directionX, (length - 1) / 2, bounds, results);
    }

    /// <summary>tileCount tiles from centre along (stepX, stepY), centre excluded.</summary>
    private static void AppendWallSide(Vector3Int centre, int stepX, int stepY, int tileCount, MapBounds bounds, List<Vector3Int> results)
    {
        var stepper = new GridLineStepper(centre, new Vector3Int(centre.X + stepX, centre.Y + stepY, centre.Z));
        for (var index = 0; index < tileCount; index++)
        {
            var tile = stepper.Next();
            AddIfOnMap(tile.X, tile.Y, tile.Z, bounds, results);
        }
    }

    /// <summary>How many eighths of a turn there are.</summary>
    private const int OctantCount = 8;

    /// <summary>The eighth of a turn, counted anticlockwise from +X (atan2 of y over x), that each unit step points toward, indexed by (stepY + 1) * 3 + (stepX + 1); the zero step has none.</summary>
    private static readonly int[] OctantOfStep = [5, 6, 7, 4, -1, 0, 3, 2, 1];

    /// <summary>The Adjacent ring around the footprint at anchor, keeping only the tiles in the eighth of a turn nearest (directionX, directionY) and the two beside it. Nothing for a zero direction.</summary>
    private static void AppendSweep(Vector3Int anchor, Vector2Byte anchorSize, int directionX, int directionY, MapBounds bounds, List<Vector3Int> results)
    {
        if (directionX == 0 && directionY == 0)
        {
            return;
        }

        var facingOctant = OctantOf(directionX, directionY);
        var firstAppended = results.Count;
        AppendAdjacent(anchor, anchorSize, bounds, results);

        var writeIndex = firstAppended;
        for (var readIndex = firstAppended; readIndex < results.Count; readIndex++)
        {
            var tile = results[readIndex];
            var sideX = tile.X < anchor.X ? -1 : tile.X >= anchor.X + anchorSize.X ? 1 : 0;
            var sideY = tile.Y < anchor.Y ? -1 : tile.Y >= anchor.Y + anchorSize.Y ? 1 : 0;
            var turnsApart = System.Math.Abs(OctantOfStep[(sideY + 1) * 3 + sideX + 1] - facingOctant);
            if (System.Math.Min(turnsApart, OctantCount - turnsApart) <= 1)
            {
                results[writeIndex++] = tile;
            }
        }

        results.RemoveRange(writeIndex, results.Count - writeIndex);
    }

    /// <summary>The eighth of a turn, counted as OctantOfStep counts them, nearest the direction (directionX, directionY), not both zero.</summary>
    /// <remarks>
    /// A direction is nearest an axis when it lies within 22.5 degrees of it: |minor| &lt; (sqrt 2 - 1) |major|, which is
    /// (|major| + |minor|)^2 &lt; 2 major^2 in whole numbers. A whole-number direction never lies exactly on that boundary.
    /// </remarks>
    private static int OctantOf(int directionX, int directionY)
    {
        var absoluteX = (long)System.Math.Abs(directionX);
        var absoluteY = (long)System.Math.Abs(directionY);
        var sum = absoluteX + absoluteY;
        var stepX = sum * sum < 2 * absoluteY * absoluteY ? 0 : System.Math.Sign(directionX);
        var stepY = sum * sum < 2 * absoluteX * absoluteX ? 0 : System.Math.Sign(directionY);
        return OctantOfStep[(stepY + 1) * 3 + stepX + 1];
    }

    /// <summary>Removes, from firstIndex on, every tile of the footprint at origin, keeping the rest in order.</summary>
    private static void RemoveFootprint(Vector3Int origin, Vector2Byte originSize, int firstIndex, List<Vector3Int> results)
    {
        var writeIndex = firstIndex;
        for (var readIndex = firstIndex; readIndex < results.Count; readIndex++)
        {
            if (!IsWithinFootprint(results[readIndex], origin, originSize))
            {
                results[writeIndex++] = results[readIndex];
            }
        }

        results.RemoveRange(writeIndex, results.Count - writeIndex);
    }

    /// <summary>Every tile from MinimumRange to MaximumRange of originSize's footprint at origin, with or without the footprint itself.</summary>
    private static void AppendRangeBand(Vector3Int origin, Vector2Byte originSize, TargetRanges ranges, bool excludesFootprint, MapBounds bounds, List<Vector3Int> results) =>
        AppendRoundedBand(origin, originSize, ranges.MaximumRange, innerRadius: ranges.MinimumRange - 1, excludesFootprint, bounds, results);

    /// <summary>Removes, from firstIndex on, every tile nearer than ranges.MinimumRange to the footprint, keeping the rest in order.</summary>
    private static void RemoveNearerThanMinimum(Vector3Int origin, Vector2Byte originSize, TargetRanges ranges, int firstIndex, List<Vector3Int> results)
    {
        var writeIndex = firstIndex;
        for (var readIndex = firstIndex; readIndex < results.Count; readIndex++)
        {
            if (!ranges.IsNearerThanMinimum(origin, originSize, results[readIndex]))
            {
                results[writeIndex++] = results[readIndex];
            }
        }

        results.RemoveRange(writeIndex, results.Count - writeIndex);
    }

    /// <summary>Every tile within a rounded circle of radius around originSize's footprint at origin, with or without the footprint itself.</summary>
    private static void AppendRoundedDisc(Vector3Int origin, Vector2Byte originSize, int radius, bool excludesFootprint, MapBounds bounds, List<Vector3Int> results) =>
        AppendRoundedBand(origin, originSize, radius, innerRadius: -1, excludesFootprint, bounds, results);

    /// <summary>Every tile within a rounded circle of radius around originSize's footprint at origin and not within one of innerRadius (a negative innerRadius leaves nothing out), with or without the footprint itself, row by row.</summary>
    /// <remarks>Each row's span is worked out from the circle (GridDistance.IsWithinRoundedRadius) and clipped to bounds, so no tile outside is ever tested.</remarks>
    private static void AppendRoundedBand(Vector3Int origin, Vector2Byte originSize, int radius, int innerRadius, bool excludesFootprint, MapBounds bounds, List<Vector3Int> results)
    {
        if (radius < 0)
        {
            return;
        }

        var lastFootprintX = origin.X + originSize.X - 1;
        var lastFootprintY = origin.Y + originSize.Y - 1;
        var firstY = System.Math.Max(origin.Y - radius, bounds.MinY);
        var lastY = System.Math.Min(lastFootprintY + radius, bounds.MaxY - 1);
        for (var y = firstY; y <= lastY; y++)
        {
            var gapY = System.Math.Max(0, System.Math.Max(origin.Y - y, y - lastFootprintY));
            var reachX = RoundedReach(radius, gapY);
            var firstX = System.Math.Max(origin.X - reachX, bounds.MinX);
            var lastX = System.Math.Min(lastFootprintX + reachX, bounds.MaxX - 1);

            var innerReachX = RoundedReach(innerRadius, gapY);
            if (innerReachX < 0 && excludesFootprint && gapY == 0)
            {
                innerReachX = 0;
            }

            if (innerReachX < 0)
            {
                AddRow(firstX, lastX, y, origin.Z, results);
                continue;
            }

            AddRow(firstX, System.Math.Min(lastX, origin.X - innerReachX - 1), y, origin.Z, results);
            AddRow(System.Math.Max(firstX, lastFootprintX + innerReachX + 1), lastX, y, origin.Z, results);
        }
    }

    /// <summary>How far past the footprint's columns a row gapY rows from them reaches inside a rounded circle of radius: -1 for not at all.</summary>
    private static int RoundedReach(int radius, int gapY)
    {
        if (!GridDistance.IsWithinRoundedRadius(0, gapY, radius))
        {
            return -1;
        }

        var limit = (long)radius * radius + radius - (long)gapY * gapY;
        var reach = (int)System.Math.Sqrt(limit);
        while ((long)reach * reach > limit)
        {
            reach--;
        }

        while ((long)(reach + 1) * (reach + 1) <= limit)
        {
            reach++;
        }

        return reach;
    }

    /// <summary>Appends the tiles from firstX to lastX, both included, of row y; nothing when lastX is before firstX.</summary>
    private static void AddRow(int firstX, int lastX, int y, int z, List<Vector3Int> results)
    {
        for (var x = firstX; x <= lastX; x++)
        {
            results.Add(new Vector3Int(x, y, z));
        }
    }

    /// <summary>Stable in-place de-duplication (first occurrence of each tile wins, relative order preserved) -- only called when Resolve actually combined more than one independently-resolved group.</summary>
    /// <remarks>Marks each tile in a grid over the results' own rectangle, so a duplicate costs one array read.</remarks>
    private static void DeduplicateInPlace(List<Vector3Int> results)
    {
        var seen = (_dedupeScratch ??= new TileMarkGrid()).ResetAround(results, firstIndex: 0);

        var writeIndex = 0;
        for (var readIndex = 0; readIndex < results.Count; readIndex++)
        {
            if (seen.TryAdd(results[readIndex]))
            {
                results[writeIndex++] = results[readIndex];
            }
        }

        results.RemoveRange(writeIndex, results.Count - writeIndex);
        seen.Release();
    }

    /// <summary>
    /// The perimeter ring of tiles surrounding the originSize footprint at origin
    /// </summary>
    /// <remarks>
    /// Chebyshevdistance &lt;= 1 from any footprint cell.
    /// Deliberately excludes every tile of the footprint, even for a Phasing/Tiny entity sharing one of those tiles
    ///
    /// SingleTileFootprint is run as a common hotpath while larger entities
    /// are given the more generic calculation.
    /// </remarks>
    private static void AppendAdjacent(Vector3Int origin, Vector2Byte originSize, MapBounds bounds, List<Vector3Int> results)
    {
        if (originSize == SingleTileFootprint)
        {
            AddIfOnMap(origin.X - 1, origin.Y - 1, origin.Z, bounds, results);
            AddIfOnMap(origin.X, origin.Y - 1, origin.Z, bounds, results);
            AddIfOnMap(origin.X + 1, origin.Y - 1, origin.Z, bounds, results);
            AddIfOnMap(origin.X - 1, origin.Y, origin.Z, bounds, results);
            AddIfOnMap(origin.X + 1, origin.Y, origin.Z, bounds, results);
            AddIfOnMap(origin.X - 1, origin.Y + 1, origin.Z, bounds, results);
            AddIfOnMap(origin.X, origin.Y + 1, origin.Z, bounds, results);
            AddIfOnMap(origin.X + 1, origin.Y + 1, origin.Z, bounds, results);
            return;
        }

        var left = origin.X - 1;
        var right = origin.X + originSize.X;
        var top = origin.Y - 1;
        var bottom = origin.Y + originSize.Y;

        for (var x = left; x <= right; x++)
        {
            AddIfOnMap(x, top, origin.Z, bounds, results);
            AddIfOnMap(x, bottom, origin.Z, bounds, results);
        }

        for (var y = origin.Y; y < origin.Y + originSize.Y; y++)
        {
            AddIfOnMap(left, y, origin.Z, bounds, results);
            AddIfOnMap(right, y, origin.Z, bounds, results);
        }
    }

    /// <summary>
    /// The footprint's own tiles -- with Adjacent (Adjacent | Self), the ring and its centre.
    /// </summary>
    private static void AppendFootprint(Vector3Int origin, Vector2Byte originSize, MapBounds bounds, List<Vector3Int> results)
    {
        for (var x = origin.X; x < origin.X + originSize.X; x++)
        {
            for (var y = origin.Y; y < origin.Y + originSize.Y; y++)
            {
                AddIfOnMap(x, y, origin.Z, bounds, results);
            }
        }
    }

    /// <summary>Bounds-checked single-cell add.</summary>
    private static void AddIfOnMap(int x, int y, int z, MapBounds bounds, List<Vector3Int> results)
    {
        if (bounds.Contains(x, y))
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
    /// The footprint cell closest to tile
    /// </summary>
    /// <remarks>
    /// The standard closest-point-on-an-axis-aligned-rectangle formula
    /// Clamp the external point onto each axis' footprint range; exact and O(1).
    /// </remarks>
    private static Vector3Int ClosestFootprintCellTo(Vector3Int origin, Vector2Byte originSize, Vector3Int tile)
    {
        var closestX = System.Math.Clamp(tile.X, origin.X, origin.X + originSize.X - 1);
        var closestY = System.Math.Clamp(tile.Y, origin.Y, origin.Y + originSize.Y - 1);
        return new Vector3Int(closestX, closestY, origin.Z);
    }

    /// <summary>
    /// A continuous ray from start through aimedTile, kept from lineOrigin on
    /// </Summary>
    /// <remarks>
    /// Bresenham's line algorithm from start, at the slope toward aimedTile and on past it. Tiles before lineOrigin (which is
    /// start or a tile on the line) are skipped; from lineOrigin on, every tile is kept, lineOrigin included, until one
    /// leaves the rounded circle of length around lineOrigin or the map. Tiles on the caster's footprint are never kept, so
    /// a line from the caster starts at its first step. Nothing when start is aimedTile (no direction).
    /// </remarks>
    private static void AppendLine(Vector3Int start, Vector3Int aimedTile, Vector3Int lineOrigin, int length, Vector3Int origin, Vector2Byte originSize, MapBounds bounds, List<Vector3Int> results)
    {
        if (start.X == aimedTile.X && start.Y == aimedTile.Y)
        {
            return;
        }

        var stepper = new GridLineStepper(start, aimedTile);
        var current = start;
        var reachedLineOrigin = start.X == lineOrigin.X && start.Y == lineOrigin.Y;

        while (true)
        {
            if (reachedLineOrigin)
            {
                if (!bounds.Contains(current.X, current.Y) || !GridDistance.IsWithinRoundedRadius(lineOrigin, current, length))
                {
                    return;
                }

                if (!IsWithinFootprint(current, origin, originSize))
                {
                    results.Add(current);
                }
            }

            current = stepper.Next();
            reachedLineOrigin |= current.X == lineOrigin.X && current.Y == lineOrigin.Y;
        }
    }

    /// <summary>
    /// Resolves a cone-shaped area of effect from apex along (directionDeltaX, directionDeltaY)
    /// </summary>
    /// <remarks>
    /// Every cone is the tabletop cone, as wide at each distance as that distance: its half-angle is arctan(1/2), about 26.57
    /// degrees (53.13 in all). Its two edges are the direction turned each way by that angle, (2x - y, 2y + x) and
    /// (2x + y, 2y - x), whole numbers, so each row's span between them is worked out exactly and only its tiles are visited.
    /// A tile exactly on an edge is inside, and so is the apex itself.
    ///
    /// Extent is the rounded circle of length around the apex, so the cone's far edge is an arc -- the same bound a
    /// same-length Line stops at (see the class doc comment's nesting note). With excludesFootprint, every tile of the
    /// caster's footprint is left out regardless of angle. Nothing for a zero direction.
    /// </remarks>
    private static void AppendCone(Vector3Int apex, int directionDeltaX, int directionDeltaY, int length, Vector3Int origin, Vector2Byte originSize, bool excludesFootprint, MapBounds bounds, List<Vector3Int> results)
    {
        if (directionDeltaX == 0 && directionDeltaY == 0)
        {
            return;
        }

        var clockwiseEdgeX = 2L * directionDeltaX + directionDeltaY;
        var clockwiseEdgeY = 2L * directionDeltaY - directionDeltaX;
        var anticlockwiseEdgeX = 2L * directionDeltaX - directionDeltaY;
        var anticlockwiseEdgeY = 2L * directionDeltaY + directionDeltaX;
        var firstOffsetY = System.Math.Max(-length, bounds.MinY - apex.Y);
        var lastOffsetY = System.Math.Min(length, bounds.MaxY - 1 - apex.Y);

        for (var offsetY = firstOffsetY; offsetY <= lastOffsetY; offsetY++)
        {
            var reachX = RoundedReach(length, System.Math.Abs(offsetY));
            long firstOffsetX = System.Math.Max(-reachX, bounds.MinX - apex.X);
            long lastOffsetX = System.Math.Min(reachX, bounds.MaxX - 1 - apex.X);
            NarrowToHalfPlane(-clockwiseEdgeY, -clockwiseEdgeX * offsetY, ref firstOffsetX, ref lastOffsetX);
            NarrowToHalfPlane(anticlockwiseEdgeY, anticlockwiseEdgeX * offsetY, ref firstOffsetX, ref lastOffsetX);

            var cellY = apex.Y + offsetY;
            for (var offsetX = firstOffsetX; offsetX <= lastOffsetX; offsetX++)
            {
                var candidate = new Vector3Int(apex.X + (int)offsetX, cellY, apex.Z);
                if (!excludesFootprint || !IsWithinFootprint(candidate, origin, originSize))
                {
                    results.Add(candidate);
                }
            }
        }
    }

    /// <summary>Narrows first..last to the whole x with coefficient x &gt;= bound: from below for a positive coefficient, from above for a negative one, to nothing for a zero one with a positive bound.</summary>
    private static void NarrowToHalfPlane(long coefficient, long bound, ref long first, ref long last)
    {
        if (coefficient > 0)
        {
            first = System.Math.Max(first, -FloorDivide(-bound, coefficient));
        }
        else if (coefficient < 0)
        {
            last = System.Math.Min(last, FloorDivide(bound, coefficient));
        }
        else if (bound > 0)
        {
            last = first - 1;
        }
    }

    private static long FloorDivide(long dividend, long divisor)
    {
        var quotient = dividend / divisor;
        return dividend % divisor != 0 && (dividend < 0) != (divisor < 0) ? quotient - 1 : quotient;
    }
}
