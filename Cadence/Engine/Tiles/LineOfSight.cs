using Engine.Math;

namespace Engine.Tiles;

/// <summary>Whether one tile can see another past walls: the one rule aiming, a shape's spread, field of view and a path's line share.</summary>
/// <remarks>
/// <para>
/// A tile is visible from another when some digital line between them (the tiles a straight line picks, one per step along
/// its longer axis) crosses no wall and takes no diagonal step between two walls touching only at their corners -- sight
/// never slips through a diagonal gap movement can't take (MovementCandidates.IsDiagonalMoveClear). Neither end's own tile
/// counts, so a wall is seen though nothing past it is. Reversing a digital line gives a digital line, so the rule is
/// symmetric: A sees B exactly when B sees A. It is exact at any distance.
/// </para>
/// <para>
/// IsVisible tests one pair, and FindLine hands back the line it found, which a path then follows (PathTracer) -- so
/// whatever can be seen can be aimed at, and a path aimed there gets there. Both try Bresenham's line first and, when it is
/// blocked, every other line at once (DigitalSight). ComputeVisible sweeps a whole area the same way, so the two always
/// agree.
/// </para>
/// </remarks>
public static class LineOfSight
{
    /// <summary>The eight eighths of a turn.</summary>
    /// <remarks>
    /// Neighbouring octants share their edge tiles, which see alike from either side, so each edge is added by one of them:
    /// the axis (column 0) by the octant whose column runs positive, the diagonal (column == depth) by the one whose depth
    /// runs along X.
    /// </remarks>
    private static readonly Octant[] Octants =
    [
        Octant.Of(1, 1, depthAlongX: true), Octant.Of(1, -1, depthAlongX: true), Octant.Of(-1, 1, depthAlongX: true), Octant.Of(-1, -1, depthAlongX: true),
        Octant.Of(1, 1, depthAlongX: false), Octant.Of(1, -1, depthAlongX: false), Octant.Of(-1, 1, depthAlongX: false), Octant.Of(-1, -1, depthAlongX: false),
    ];

    /// <summary>Whether to is visible from from, on from's map layer.</summary>
    public static bool IsVisible(Vector3Int from, Vector3Int to, ISightBlockers walls) => FindLine(from, to, walls, out _);

    /// <summary>Whether tile is visible from any tile of the footprint at origin.</summary>
    /// <remarks>The footprint tile nearest tile is tried first, since it is the likeliest to see it.</remarks>
    public static bool IsVisibleFromFootprint(Vector3Int origin, Vector2Byte originSize, Vector3Int tile, ISightBlockers walls) =>
        FindLineFromFootprint(origin, originSize, tile, walls, out _);

    /// <summary>The line from a tile of the footprint at origin to tile that sight would see along, if any: false, with the nearest footprint tile's Bresenham line, when walls hide tile from every footprint tile.</summary>
    /// <remarks>Footprint tiles are tried nearest tile first. With no walls, the nearest tile's Bresenham line.</remarks>
    internal static bool FindLineFromFootprint(Vector3Int origin, Vector2Byte originSize, Vector3Int tile, ISightBlockers? walls, out DigitalLine line)
    {
        var nearestX = System.Math.Clamp(tile.X, origin.X, origin.X + originSize.X - 1);
        var nearestY = System.Math.Clamp(tile.Y, origin.Y, origin.Y + originSize.Y - 1);
        var nearest = new Vector3Int(nearestX, nearestY, origin.Z);
        if (FindLine(nearest, tile, walls, out line))
        {
            return true;
        }

        for (var x = origin.X; x < origin.X + originSize.X; x++)
        {
            for (var y = origin.Y; y < origin.Y + originSize.Y; y++)
            {
                if ((x != nearestX || y != nearestY) && FindLine(new Vector3Int(x, y, origin.Z), tile, walls, out var other))
                {
                    line = other;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>A line from from to to that crosses no wall and slips through no corner gap -- Bresenham's when it is clear, else the one nearest the straight line there -- or false, with Bresenham's, when there is none.</summary>
    /// <remarks>With no walls, Bresenham's line. A line to from itself has no direction: true, with a line that never moves off from.</remarks>
    internal static bool FindLine(Vector3Int from, Vector3Int to, ISightBlockers? walls, out DigitalLine line)
    {
        var deltaX = to.X - from.X;
        var deltaY = to.Y - from.Y;
        var depthAlongX = System.Math.Abs(deltaX) >= System.Math.Abs(deltaY);
        var octant = Octant.Of(deltaX < 0 ? -1 : 1, deltaY < 0 ? -1 : 1, depthAlongX);
        var depth = depthAlongX ? System.Math.Abs(deltaX) : System.Math.Abs(deltaY);
        var column = depthAlongX ? System.Math.Abs(deltaY) : System.Math.Abs(deltaX);
        if (depth == 0)
        {
            line = new DigitalLine(from, octant, Rise: 0, Run: 1, Offset: 0);
            return true;
        }

        var (rise, run, offset) = BresenhamLine(depth, column, depthAlongX);
        line = new DigitalLine(from, octant, rise, run, offset);
        if (walls is null || IsClear(line, depth, walls))
        {
            return true;
        }

        if (!DigitalSight.TryFindLine(from, octant, depth, column, walls, out var foundRise, out var foundRun, out var foundOffset))
        {
            return false;
        }

        line = new DigitalLine(from, octant, foundRise, foundRun, foundOffset);
        return true;
    }

    /// <summary>The digital line Bresenham's line (GridLineStepper, which a Line shape is laid out along) draws to (depth, column): the first line sight tries, and the one a path follows when nothing gets through.</summary>
    /// <remarks>
    /// Bresenham's line rounds each step to the nearest column, breaking a half-tile tie one way or the other depending on
    /// which axis is the longer, so it is the digital line of slope column / depth whose offset is half the depth, rounded
    /// one way or the other; the tie-break is read off by stepping it once. Carried on past (depth, column), it stays the
    /// line GridLineStepper carries on along.
    /// </remarks>
    private static (long Rise, long Run, long Offset) BresenhamLine(int depth, int column, bool depthAlongX)
    {
        var halfUpOffset = depth / 2;
        var halfDownOffset = (depth - 1) / 2;
        var through = depthAlongX ? new Vector3Int(depth, column, 0) : new Vector3Int(column, depth, 0);
        var stepper = new GridLineStepper(default, through);
        var roundsHalfUp = true;
        for (var step = 1; step <= depth && roundsHalfUp; step++)
        {
            var tile = stepper.Next();
            roundsHalfUp = (depthAlongX ? tile.Y : tile.X) == ((long)column * step + halfUpOffset) / depth;
        }

        var divisor = GreatestCommonDivisor(column, depth);
        return (column / divisor, depth / divisor, (roundsHalfUp ? halfUpOffset : halfDownOffset) / divisor);
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return System.Math.Max(a, 1);
    }

    /// <summary>Whether line, from its start to depth, crosses no wall before depth and takes no diagonal step between two walls touching only at their corners.</summary>
    private static bool IsClear(DigitalLine line, int depth, ISightBlockers walls)
    {
        var stepper = line.Stepper();
        var previous = line.Start;
        for (var step = 1; step <= depth; step++)
        {
            var next = stepper.Next();
            if (next.X != previous.X && next.Y != previous.Y &&
                walls.BlocksSight(next.X, previous.Y, previous.Z) && walls.BlocksSight(previous.X, next.Y, previous.Z))
            {
                return false;
            }

            if (step < depth && walls.BlocksSight(next.X, next.Y, next.Z))
            {
                return false;
            }

            previous = next;
        }

        return true;
    }

    /// <summary>Adds to visible every tile within Chebyshev distance radius of origin that origin can see, origin included.</summary>
    /// <remarks>visible is not cleared, so several origins (a large caster's footprint) can add to one set.</remarks>
    public static void ComputeVisible(Vector3Int origin, int radius, ISightBlockers walls, HashSet<Vector3Int> visible)
    {
        var sink = new HashSetSink(visible);
        ComputeVisible(origin, radius, walls, ref sink);
    }

    /// <summary>ComputeVisible, handing each visible tile to sink.</summary>
    /// <remarks>Each octant is swept along every digital line at once (DigitalSight.AddVisible), at a cost that grows with the area swept.</remarks>
    internal static void ComputeVisible<TSink>(Vector3Int origin, int radius, ISightBlockers walls, ref TSink sink)
        where TSink : struct, IVisibleTileSink
    {
        sink.Add(origin);
        if (radius <= 0)
        {
            return;
        }

        foreach (var octant in Octants)
        {
            DigitalSight.AddVisible(origin, octant, radius, walls, ref sink);
        }
    }

    /// <summary>Receives the tiles ComputeVisible finds visible.</summary>
    internal interface IVisibleTileSink
    {
        void Add(Vector3Int tile);
    }

    private readonly struct HashSetSink(HashSet<Vector3Int> visible) : IVisibleTileSink
    {
        public void Add(Vector3Int tile) => visible.Add(tile);
    }

    /// <summary>One eighth of a turn: the map step one depth and one column take, and which of its edges it adds.</summary>
    internal readonly record struct Octant(int DepthStepX, int DepthStepY, int ColumnStepX, int ColumnStepY, bool AddsAxis, bool AddsDiagonal)
    {
        public static Octant Of(int signX, int signY, bool depthAlongX) =>
            depthAlongX
                ? new Octant(signX, 0, 0, signY, AddsAxis: signY > 0, AddsDiagonal: true)
                : new Octant(0, signY, signX, 0, AddsAxis: signX > 0, AddsDiagonal: false);

        public Vector3Int TileAt(Vector3Int origin, int depth, int column) =>
            new(origin.X + (DepthStepX * depth) + (ColumnStepX * column), origin.Y + (DepthStepY * depth) + (ColumnStepY * column), origin.Z);
    }
}

/// <summary>One digital line out of a start tile: column(depth) = floor((Rise depth + Offset) / Run) in Octant's coordinates, carried on past wherever it was found to.</summary>
internal readonly record struct DigitalLine(Vector3Int Start, LineOfSight.Octant Octant, long Rise, long Run, long Offset)
{
    /// <summary>A stepper at Start, its first Next the line's next tile.</summary>
    public DigitalLineStepper Stepper() => new(Start, Octant.DepthStepX, Octant.DepthStepY, Octant.ColumnStepX, Octant.ColumnStepY, Rise, Run, Offset);
}

/// <summary>Walks a digital line one tile per Next, and can mirror it off a wall without losing its place along the line.</summary>
internal struct DigitalLineStepper(Vector3Int start, int depthStepX, int depthStepY, int columnStepX, int columnStepY, long rise, long run, long offset)
{
    private int _x = start.X;
    private int _y = start.Y;
    private int _depthStepX = depthStepX;
    private int _depthStepY = depthStepY;
    private int _columnStepX = columnStepX;
    private int _columnStepY = columnStepY;
    private long _error = offset;

    /// <summary>The next tile along the line.</summary>
    public Vector3Int Next()
    {
        _x += _depthStepX;
        _y += _depthStepY;
        _error += rise;
        if (_error >= run)
        {
            _error -= run;
            _x += _columnStepX;
            _y += _columnStepY;
        }

        return new Vector3Int(_x, _y, start.Z);
    }

    /// <summary>Mirrors the rest of the line across X, Y or both, from where it is now: a bounce.</summary>
    public void Reflect(bool acrossX, bool acrossY)
    {
        if (acrossX)
        {
            _depthStepX = -_depthStepX;
            _columnStepX = -_columnStepX;
        }

        if (acrossY)
        {
            _depthStepY = -_depthStepY;
            _columnStepY = -_columnStepY;
        }
    }
}
