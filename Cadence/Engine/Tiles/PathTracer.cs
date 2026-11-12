using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Engine.Math;

namespace Engine.Tiles;

/// <summary>Why a traced path stopped.</summary>
public enum PathStopReason : byte
{
    /// <summary>Still going.</summary>
    None,

    /// <summary>It reached the aimed tile: a Lobbed path, or a Projectile with ProjectilePathEnd.AtAimedTile.</summary>
    AimedTile,

    /// <summary>It flew its maximum range.</summary>
    MaximumRange,

    /// <summary>A wall stopped it on the last open tile before it.</summary>
    Wall,

    /// <summary>It met the entity that reached its hit count.</summary>
    Entity,

    /// <summary>The next tile was off the map.</summary>
    MapEdge,

    /// <summary>The aim is on the caster's own footprint, so the path has no direction and ends where it starts.</summary>
    NoDirection,
}

/// <summary>Who a path meets on a tile: the one entity there that can stop or be hit by a projectile.</summary>
public interface IPathOccupants
{
    /// <summary>The entity on tile a projectile meets, or -1 for none.</summary>
    int EntityMetAt(Vector3Int tile);
}

/// <summary>What a path is and where it is going: everything a PathTracer needs besides the walls, the map and who stands where.</summary>
/// <param name="Path">Projectile or Lobbed (a Placed effect has no path to trace).</param>
/// <param name="PathEnd">Where a Projectile stops when nothing stops it earlier; a Lobbed path always lands on the aimed tile.</param>
/// <param name="HitCount">How many entities a Projectile hits before it stops on the last; one it can never reach (ushort.MaxValue) hits every entity on the way. A Lobbed path meets nobody.</param>
/// <param name="PathWalls">What the path does on reaching a wall.</param>
/// <param name="WallBounceCount">How many times a path that bounces off walls may bounce before a wall stops it.</param>
/// <param name="Origin">The caster's footprint origin.</param>
/// <param name="OriginSize">The caster's footprint size.</param>
/// <param name="AimedTile">The tile aimed at.</param>
/// <param name="Ranges">MaximumRange is how far the path may fly; nothing nearer the caster than MinimumRange is met (the projectile arms after it).</param>
public readonly record struct TargetPathRequest(
    TargetPath Path,
    ProjectilePathEnd PathEnd,
    ushort HitCount,
    PathWallRule PathWalls,
    byte WallBounceCount,
    Vector3Int Origin,
    Vector2Byte OriginSize,
    Vector3Int AimedTile,
    TargetRanges Ranges);

/// <summary>Where a traced path ended and why.</summary>
public readonly record struct PathTrace(Vector3Int EndTile, PathStopReason StopReason);

/// <summary>Walks a Projectile or Lobbed path one tile at a time, deciding at each step whether it goes on, bounces off a wall or stops.</summary>
/// <remarks>
/// <para>
/// The path is the digital line sight would see the aim along (LineOfSight.FindLineFromFootprint: from the footprint tile
/// nearest the aim when it can, past walls the path doesn't pass through), through the aim and on past it; when no line
/// gets there, the nearest tile's straightest, into whatever stops it. So a path reaches whatever can be seen and isn't a
/// wall, and every tile on its way is one sight reaches too. The
/// caster's own footprint is never part of it and never met. A wall (ISightBlockers), or a diagonal step between two walls
/// touching only at their corners, stops it on the last open tile before
/// it, lets it through, or reflects it (PathWallRule). Its first leg's range is measured from the caster's footprint, as the
/// aim's is; a path that bounces measures its range along every leg it flew. A
/// Projectile meets each entity IPathOccupants reports, once, in order -- whatever its effects will do to it -- and stops on
/// the one that reaches its hit count; nothing nearer than MinimumRange is met. A Lobbed path meets nobody.
/// </para>
/// <para>
/// Resolution with no travel time runs it to the end in one call (Trace); something that flies over several frames keeps a
/// tracer and calls TryStep a few times a frame. Hit counts, wall rules and ranges live here once, for both.
/// </para>
/// </remarks>
public struct PathTracer
{
    private readonly TargetPathRequest _request;
    private readonly ISightBlockers? _walls;
    private readonly MapBounds _bounds;
    private readonly int _maximumSteps;
    private readonly double _maximumFlightLength;
    private DigitalLineStepper _stepper;
    private Vector3Int _legStart;
    private double _earlierLegsLength;
    private bool _hasBounced;
    private int _bouncesLeft;
    private int _steps;

    public PathTracer(in TargetPathRequest request, ISightBlockers? walls, MapBounds bounds)
        : this(in request, walls, bounds, LineFor(in request, walls))
    {
    }

    /// <summary>A tracer along line, which must be the one LineFor finds for request: a caller that has already found it passes it rather than searching again.</summary>
    internal PathTracer(in TargetPathRequest request, ISightBlockers? walls, MapBounds bounds, DigitalLine line)
    {
        _request = request;
        _walls = walls;
        _bounds = bounds;
        _bouncesLeft = request.PathWalls == PathWallRule.BouncesOffWalls ? request.WallBounceCount : 0;
        _maximumSteps = (System.Math.Max(0, request.Ranges.MaximumRange) + request.OriginSize.X + request.OriginSize.Y + 2) * (_bouncesLeft + 1) * 2;
        _maximumFlightLength = System.Math.Sqrt(((double)request.Ranges.MaximumRange * request.Ranges.MaximumRange) + request.Ranges.MaximumRange);

        var aimTile = new Vector3Int(request.AimedTile.X, request.AimedTile.Y, request.Origin.Z);
        Current = line.Start;
        _legStart = line.Start;
        _stepper = line.Stepper();

        if (IsOnCasterFootprint(aimTile))
        {
            StopReason = PathStopReason.NoDirection;
        }
    }

    /// <summary>The tile the path is on: where it started, then the last tile it reached, and where it ended once it has stopped.</summary>
    public Vector3Int Current { get; private set; }

    /// <summary>Why the path stopped, or None while it is still going.</summary>
    public PathStopReason StopReason { get; private set; }

    public readonly bool IsFinished => StopReason != PathStopReason.None;

    /// <summary>Whether request's path is turned or stopped by walls: every path but one that passes through them.</summary>
    internal static bool FollowsSightPastWalls(in TargetPathRequest request) => request.PathWalls != PathWallRule.PassesThroughWalls;

    /// <summary>The line request's path follows: the one sight sees its aim along, from the footprint tile nearest it when it can -- past walls when it FollowsSightPastWalls, else Bresenham's.</summary>
    private static DigitalLine LineFor(in TargetPathRequest request, ISightBlockers? walls)
    {
        var aimTile = new Vector3Int(request.AimedTile.X, request.AimedTile.Y, request.Origin.Z);
        LineOfSight.FindLineFromFootprint(request.Origin, request.OriginSize, aimTile, FollowsSightPastWalls(in request) ? walls : null, out var line);
        return line;
    }

    /// <summary>Runs request's path to its end, filling pathTiles with every tile it passed through, the end included, and metEntities with the entities it met, in order.</summary>
    /// <remarks>Both lists are cleared first.</remarks>
    public static PathTrace Trace(in TargetPathRequest request, ISightBlockers? walls, MapBounds bounds, IPathOccupants? occupants, List<Vector3Int> pathTiles, List<int> metEntities)
    {
        pathTiles.Clear();
        metEntities.Clear();
        var tracer = new PathTracer(in request, walls, bounds);
        return tracer.RunToEnd(occupants, pathTiles, metEntities);
    }

    /// <summary>Steps to the end, adding every tile reached to pathTiles and every entity met to metEntities.</summary>
    internal PathTrace RunToEnd(IPathOccupants? occupants, List<Vector3Int> pathTiles, List<int> metEntities)
    {
        while (TryStep(occupants, metEntities, out var tile))
        {
            pathTiles.Add(tile);
        }

        return new PathTrace(Current, StopReason);
    }

    /// <summary>Appends to tiles every tile a Projectile like request could hit on its way, aimed in turn at each of aimedTiles: the tiles its path passes through past MinimumRange, each once and only where tiles doesn't hold it already.</summary>
    /// <remarks>
    /// Walls alone decide where each path goes -- it meets nobody, so nothing stops it short -- and request's own AimedTile
    /// is ignored. A Lobbed path arcs over everyone on the way, so it appends nothing.
    /// </remarks>
    public static void AppendTilesPassed(in TargetPathRequest request, ReadOnlySpan<Vector3Int> aimedTiles, ISightBlockers? walls, MapBounds bounds, List<Vector3Int> tiles)
    {
        if (request.Path != TargetPath.Projectile || aimedTiles.IsEmpty)
        {
            return;
        }

        var origin = request.Origin;
        var originSize = request.OriginSize;
        var reach = System.Math.Max(request.Ranges.MaximumRange, 0) + 1;
        var kept = (_passedScratch ??= new TileMarkGrid())
            .Reset(origin.X - reach, origin.Y - reach, originSize.X + 2 * reach, originSize.Y + 2 * reach);
        foreach (var tile in CollectionsMarshal.AsSpan(tiles))
        {
            kept.Add(tile);
        }

        var cachedWalls = walls is null ? null : SightBlockerCache.Around(walls, origin, originSize, reach);
        try
        {
            foreach (var aimedTile in aimedTiles)
            {
                var aimedRequest = request with { AimedTile = aimedTile };
                var tracer = new PathTracer(in aimedRequest, cachedWalls, bounds);
                while (tracer.TryStep(occupants: null, NoEntitiesMet, out var tile))
                {
                    if (!request.Ranges.IsNearerThanMinimum(origin, originSize, tile) && kept.TryAdd(tile))
                    {
                        tiles.Add(tile);
                    }
                }
            }
        }
        finally
        {
            cachedWalls?.Release();
            kept.Release();
        }
    }

    /// <summary>Reused across calls by AppendTilesPassed. [ThreadStatic] defensively, like TargetShapeResolver's scratch.</summary>
    [ThreadStatic]
    private static TileMarkGrid? _passedScratch;

    /// <summary>The met-entities list AppendTilesPassed hands TryStep: with no occupants given, a path meets nobody, so it stays empty.</summary>
    private static readonly List<int> NoEntitiesMet = [];

    /// <summary>Moves the path one tile on, meeting whoever is there; false, without moving, once it has stopped or stops before the next tile.</summary>
    /// <param name="metEntities">The entities met so far; one this step meets is added.</param>
    /// <param name="tile">The tile reached.</param>
    public bool TryStep(IPathOccupants? occupants, List<int> metEntities, out Vector3Int tile)
    {
        while (true)
        {
            tile = Current;
            if (IsFinished)
            {
                return false;
            }

            if (++_steps > _maximumSteps)
            {
                StopReason = PathStopReason.MaximumRange;
                return false;
            }

            Unsafe.SkipInit(out DigitalLineStepper beforeStep);
            if (_bouncesLeft > 0)
            {
                beforeStep = _stepper;
            }

            var next = _stepper.Next();
            if (!_bounds.Contains(next.X, next.Y))
            {
                StopReason = PathStopReason.MapEdge;
                return false;
            }

            if (_walls is not null && _request.PathWalls != PathWallRule.PassesThroughWalls && IsBlockedStep(next))
            {
                if (_bouncesLeft == 0)
                {
                    StopReason = PathStopReason.Wall;
                    return false;
                }

                _stepper = beforeStep;
                Bounce(next);
                continue;
            }

            if (!IsWithinFlightRange(next))
            {
                StopReason = PathStopReason.MaximumRange;
                return false;
            }

            Current = next;
            if (IsOnCasterFootprint(next))
            {
                continue;
            }

            tile = next;
            if (Meets(next, occupants, metEntities))
            {
                StopReason = PathStopReason.Entity;
            }
            else if ((_request.Path == TargetPath.Lobbed || _request.PathEnd == ProjectilePathEnd.AtAimedTile) && next.X == _request.AimedTile.X && next.Y == _request.AimedTile.Y)
            {
                StopReason = PathStopReason.AimedTile;
            }

            return true;
        }
    }

    /// <summary>Whether the step from Current to next hits a wall: next is one, or it slips diagonally between two walls touching only at their corners.</summary>
    private readonly bool IsBlockedStep(Vector3Int next)
    {
        if (_walls!.BlocksSight(next.X, next.Y, next.Z))
        {
            return true;
        }

        return next.X != Current.X && next.Y != Current.Y &&
            _walls.BlocksSight(next.X, Current.Y, Current.Z) && _walls.BlocksSight(Current.X, next.Y, Current.Z);
    }

    /// <summary>Whether next meets an entity, adding it to metEntities, and that entity reaches the hit count.</summary>
    private readonly bool Meets(Vector3Int next, IPathOccupants? occupants, List<int> metEntities)
    {
        if (_request.Path != TargetPath.Projectile || _request.HitCount == 0 || occupants is null || _request.Ranges.IsNearerThanMinimum(_request.Origin, _request.OriginSize, next))
        {
            return false;
        }

        var entityId = occupants.EntityMetAt(next);
        if (entityId < 0 || metEntities.Contains(entityId))
        {
            return false;
        }

        metEntities.Add(entityId);
        return metEntities.Count >= _request.HitCount;
    }

    /// <summary>Reflects the path off the wall at blocked, from Current: across whichever axis the wall faces, or straight back into a corner.</summary>
    /// <remarks>The line is mirrored where it stands, keeping its place along the line, so the reflected leg is the same digital line turned.</remarks>
    private void Bounce(Vector3Int blocked)
    {
        var stepX = blocked.X - Current.X;
        var stepY = blocked.Y - Current.Y;
        var flipsX = stepX != 0;
        var flipsY = stepY != 0;
        if (stepX != 0 && stepY != 0)
        {
            var wallAcrossX = _walls!.BlocksSight(Current.X + stepX, Current.Y, Current.Z);
            var wallAcrossY = _walls.BlocksSight(Current.X, Current.Y + stepY, Current.Z);
            flipsX = wallAcrossX || !wallAcrossY;
            flipsY = wallAcrossY || !wallAcrossX;
        }

        _earlierLegsLength += LegLength(Current);
        _hasBounced = true;
        _legStart = Current;
        _stepper.Reflect(flipsX, flipsY);
        _bouncesLeft--;
    }

    /// <summary>Whether tile is within MaximumRange along the path: the rounded circle (GridDistance.IsWithinRoundedRadius) around the caster's footprint on the first leg, as the aim is measured, the legs' summed lengths after a bounce.</summary>
    private readonly bool IsWithinFlightRange(Vector3Int tile)
    {
        if (!_hasBounced)
        {
            return GridDistance.IsWithinRoundedRadius(_request.Origin, _request.OriginSize, tile, _request.Ranges.MaximumRange);
        }

        return _earlierLegsLength + LegLength(tile) <= _maximumFlightLength;
    }

    /// <summary>How far the current leg has flown to tile: from the caster's footprint on the first leg, from the last bounce after.</summary>
    private readonly double LegLength(Vector3Int tile)
    {
        var deltaX = _hasBounced ? tile.X - _legStart.X : GapFromFootprint(tile.X, _request.Origin.X, _request.OriginSize.X);
        var deltaY = _hasBounced ? tile.Y - _legStart.Y : GapFromFootprint(tile.Y, _request.Origin.Y, _request.OriginSize.Y);
        return System.Math.Sqrt(((double)deltaX * deltaX) + ((double)deltaY * deltaY));
    }

    private static int GapFromFootprint(int coordinate, int footprintStart, int footprintLength) =>
        System.Math.Max(0, System.Math.Max(footprintStart - coordinate, coordinate - (footprintStart + footprintLength - 1)));

    private readonly bool IsOnCasterFootprint(Vector3Int tile) =>
        tile.X >= _request.Origin.X && tile.X < _request.Origin.X + _request.OriginSize.X &&
        tile.Y >= _request.Origin.Y && tile.Y < _request.Origin.Y + _request.OriginSize.Y;
}
