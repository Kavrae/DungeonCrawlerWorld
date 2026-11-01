using Engine.Math;

namespace Engine.Tiles;

/// <summary>Sight along digital lines, worked out on the lines themselves: which tiles some line reaches, and a line that reaches a given tile.</summary>
/// <remarks>
/// <para>
/// In an octant's own coordinates a tile is (depth, column), and every digital line is column(i) = floor(s i + t) for a real
/// slope 0 &lt;= s &lt;= 1 and offset 0 &lt;= t &lt; 1. So the lines still in play are a region of the (s, t) square, kept as
/// a few convex views (LineView). Each depth, a view's lines reach the columns between its smallest and largest s depth + t;
/// a wall there removes the band of lines y &lt;= s depth + t &lt; y + 1 that would pass through it (the wall itself still
/// seen), and a diagonal gap between two walls touching at their corners removes the column only it leads into (a line
/// reaches column y at depth x past a wall at (x - 1, y) only by stepping across from y - 1, so with a wall at (x, y - 1)
/// too, every line left there came through the gap). Views split where walls cut them and end when nothing is left.
/// </para>
/// <para>
/// Every line in a view has passed every wall and gap so far, and every line that has is in one, so a tile is reached
/// exactly when some view's lines reach it: the same answer as trying every digital line, at a cost that grows with the
/// tiles swept rather than the lines. A view of positive area always holds whole digital lines -- a run of tiles a line picks
/// is picked by an open set of (s, t) -- so views are kept closed and dropped once they have no area. Every corner is
/// where two of the lines s = 0, s = 1, t = 0, t = 1 and x s + t = y meet, so coordinates are small fractions, compared
/// exactly.
/// </para>
/// </remarks>
internal static class DigitalSight
{
    [ThreadStatic]
    private static Scratch? _scratch;

    /// <summary>Hands sink every tile of octant out to radius from origin that some line reaches past walls (origin itself excluded), keeping to the octant's own edges.</summary>
    public static void AddVisible<TSink>(Vector3Int origin, LineOfSight.Octant octant, int radius, ISightBlockers walls, ref TSink sink)
        where TSink : struct, LineOfSight.IVisibleTileSink
    {
        var scratch = (_scratch ??= new Scratch()).Begin(origin, octant, walls, radius);
        scratch.Views.Add(scratch.TakeSquare());
        for (var depth = 1; depth <= radius && scratch.Views.Count > 0; depth++)
        {
            scratch.Rows.Advance(depth);
            foreach (var view in scratch.Views)
            {
                scratch.Step(view, depth, marks: true, ref sink);
            }

            scratch.EndDepth();
        }

        scratch.End();
    }

    /// <summary>Whether some line from origin reaches (depth, column) of octant past walls, depth &gt;= 1; when one does, the one nearest the straight line there, as column(i) = floor((rise i + offset) / run).</summary>
    public static bool TryFindLine(Vector3Int origin, LineOfSight.Octant octant, int depth, int column, ISightBlockers walls, out long rise, out long run, out long offset)
    {
        rise = 0;
        run = 1;
        offset = 0;
        var scratch = (_scratch ??= new Scratch()).Begin(origin, octant, walls, depth);
        var square = scratch.TakeSquare();
        var below = scratch.Take();
        var through = scratch.Take();
        var reached = square.ClipInto(below, depth, 1, column + 1) && below.ClipInto(through, -depth, -1, -column);
        scratch.Give(square);
        scratch.Give(below);
        if (!reached)
        {
            scratch.Give(through);
            scratch.End();
            return false;
        }

        scratch.Views.Add(through);
        var noSink = default(NoSink);
        for (var x = 1; x < depth && scratch.Views.Count > 0; x++)
        {
            scratch.Rows.Advance(x);
            foreach (var view in scratch.Views)
            {
                scratch.Step(view, x, marks: false, ref noSink);
            }

            scratch.EndDepth();
        }

        scratch.Rows.Advance(depth);
        var found = scratch.Views.Count > 0 && !scratch.IsCut(depth, column);
        if (found)
        {
            var best = scratch.Views[0];
            var bestGap = double.MaxValue;
            foreach (var view in scratch.Views)
            {
                var gap = System.Math.Abs(view.CentreSlope() - (double)column / depth);
                if (gap < bestGap)
                {
                    best = view;
                    bestGap = gap;
                }
            }

            best.InteriorLine(out rise, out run, out offset);
        }

        scratch.End();
        return found;
    }

    private readonly struct NoSink : LineOfSight.IVisibleTileSink
    {
        public void Add(Vector3Int tile)
        {
        }
    }

    /// <summary>One sweep's working state, reused per thread: the views in play, the ones made for the next depth, spare views, and the walls of the two rows a step reads.</summary>
    private sealed class Scratch
    {
        private readonly List<LineView> _spare = [];
        private List<LineView> _next = [];
        private int[] _addedStamp = [];
        private int _addedRowLength;
        private int _addedSweep;

        public List<LineView> Views { get; private set; } = [];

        public RowWalls Rows { get; } = new();

        private Vector3Int _origin;
        private LineOfSight.Octant _octant;

        public Scratch Begin(Vector3Int origin, LineOfSight.Octant octant, ISightBlockers walls, int radius)
        {
            _origin = origin;
            _octant = octant;
            Rows.Begin(origin, octant, walls, radius);
            _addedRowLength = radius + 2;
            var tiles = (long)_addedRowLength * (radius + 1);
            if (tiles <= ScratchRetention.KeptCellCount && _addedStamp.Length < tiles)
            {
                _addedStamp = new int[tiles];
            }

            if (++_addedSweep == int.MaxValue)
            {
                Array.Clear(_addedStamp);
                _addedSweep = 1;
            }

            return this;
        }

        public LineView Take()
        {
            if (_spare.Count == 0)
            {
                return new LineView();
            }

            var view = _spare[^1];
            _spare.RemoveAt(_spare.Count - 1);
            return view;
        }

        public LineView TakeSquare()
        {
            var view = Take();
            view.SetToSquare();
            return view;
        }

        public void Give(LineView view) => _spare.Add(view);

        /// <summary>Whether a line reaches column at depth only through a diagonal gap between two walls touching at their corners.</summary>
        public bool IsCut(int depth, int column) => column > 0 && Rows.IsWall(depth, column - 1) && Rows.IsWall(depth - 1, column);

        /// <summary>Takes view through depth: hands sink the tiles its lines reach (when marking), and puts what is left of it, split around walls and gaps, into the next depth's views.</summary>
        public void Step<TSink>(LineView view, int depth, bool marks, ref TSink sink)
            where TSink : struct, LineOfSight.IVisibleTileSink
        {
            view.ColumnRange(depth, out var firstColumn, out var lastColumn);
            firstColumn = System.Math.Max(firstColumn, 0);
            lastColumn = System.Math.Min(lastColumn, depth);
            var runStart = -1;
            var keptWhole = false;
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var passes = false;
                if (!IsCut(depth, column))
                {
                    if (marks)
                    {
                        Mark(depth, column, ref sink);
                    }

                    passes = !Rows.IsWall(depth, column);
                }

                if (passes)
                {
                    if (runStart < 0)
                    {
                        runStart = column;
                    }

                    continue;
                }

                if (runStart >= 0)
                {
                    keptWhole |= KeepRun(view, depth, runStart, column - 1, firstColumn, lastColumn);
                    runStart = -1;
                }
            }

            if (runStart >= 0)
            {
                keptWhole |= KeepRun(view, depth, runStart, lastColumn, firstColumn, lastColumn);
            }

            if (!keptWhole)
            {
                Give(view);
            }
        }

        /// <summary>Puts the part of view whose lines pass columns firstRunColumn to lastRunColumn at depth into the next depth's views; true when that is the whole view, handed on as it is.</summary>
        private bool KeepRun(LineView view, int depth, int firstRunColumn, int lastRunColumn, int firstColumn, int lastColumn)
        {
            var clipsBelow = firstRunColumn > firstColumn;
            var clipsAbove = lastRunColumn < lastColumn;
            if (!clipsBelow && !clipsAbove)
            {
                _next.Add(view);
                return true;
            }

            var piece = Take();
            var kept = clipsAbove
                ? view.ClipInto(piece, depth, 1, lastRunColumn + 1)
                : view.ClipInto(piece, -depth, -1, -firstRunColumn);
            if (kept && clipsAbove && clipsBelow)
            {
                var lower = Take();
                kept = piece.ClipInto(lower, -depth, -1, -firstRunColumn);
                Give(piece);
                piece = lower;
            }

            if (kept)
            {
                _next.Add(piece);
            }
            else
            {
                Give(piece);
            }

            return false;
        }

        private void Mark<TSink>(int depth, int column, ref TSink sink)
            where TSink : struct, LineOfSight.IVisibleTileSink
        {
            if ((column == 0 && !_octant.AddsAxis) || (column == depth && !_octant.AddsDiagonal))
            {
                return;
            }

            var tile = (long)depth * _addedRowLength + column;
            if (tile < _addedStamp.Length)
            {
                if (_addedStamp[tile] == _addedSweep)
                {
                    return;
                }

                _addedStamp[tile] = _addedSweep;
            }

            sink.Add(_octant.TileAt(_origin, depth, column));
        }

        /// <summary>Makes the next depth's views the ones in play.</summary>
        public void EndDepth()
        {
            Views.Clear();
            (Views, _next) = (_next, Views);
        }

        /// <summary>Returns every view still in play to the spares and lets go of the walls.</summary>
        public void End()
        {
            foreach (var view in Views)
            {
                Give(view);
            }

            Views.Clear();
            Rows.End();
            if (_addedStamp.Length > ScratchRetention.KeptCellCount)
            {
                _addedStamp = [];
            }
        }
    }

    /// <summary>Which tiles of the row a step is on, and the row before it, are walls: each asked of the walls at most once per sweep.</summary>
    internal sealed class RowWalls
    {
        private const byte Unknown = 0;
        private const byte Open = 1;
        private const byte Blocked = 2;

        private byte[] _current = [];
        private byte[] _previous = [];
        private int _currentDepth;
        private Vector3Int _origin;
        private LineOfSight.Octant _octant;
        private ISightBlockers? _walls;

        public void Begin(Vector3Int origin, LineOfSight.Octant octant, ISightBlockers walls, int radius)
        {
            _origin = origin;
            _octant = octant;
            _walls = walls;
            if (_current.Length < radius + 3)
            {
                _current = new byte[radius + 3];
                _previous = new byte[radius + 3];
            }

            Array.Clear(_current);
            _currentDepth = 0;
        }

        /// <summary>Moves on to depth, depth - 1 becoming the previous row.</summary>
        public void Advance(int depth)
        {
            (_previous, _current) = (_current, _previous);
            Array.Clear(_current, 0, System.Math.Min(_current.Length, depth + 3));
            _currentDepth = depth;
        }

        /// <summary>Whether (depth, column) is a wall; depth is the current row or the one before it, 0 &lt;= column &lt;= depth + 1.</summary>
        public bool IsWall(int depth, int column)
        {
            var row = depth == _currentDepth ? _current : _previous;
            ref var state = ref row[column];
            if (state == Unknown)
            {
                var tile = _octant.TileAt(_origin, depth, column);
                state = _walls!.BlocksSight(tile.X, tile.Y, tile.Z) ? Blocked : Open;
            }

            return state == Blocked;
        }

        public void End()
        {
            _walls = null;
            if (_current.Length > ScratchRetention.KeptCellCount)
            {
                _current = [];
                _previous = [];
            }
        }
    }

    /// <summary>A convex region of lines column(i) = floor(s i + t): the (s, t) meeting every constraint a s + b t &lt;= c it holds, in order around it, each corner where one meets the next.</summary>
    internal sealed class LineView
    {
        private long[] _a = new long[8];
        private long[] _b = new long[8];
        private long[] _c = new long[8];
        private long[] _sNumerator = new long[8];
        private long[] _tNumerator = new long[8];
        private long[] _denominator = new long[8];
        private int _count;

        /// <summary>The largest coefficient a or c of any constraint the view holds, which bounds how large its corners' numbers get (Cross).</summary>
        private long _largestCoefficient;

        /// <summary>The largest coefficient Cross works out a turn for in a long.</summary>
        private const long NarrowCoefficientLimit = 2048;

        /// <summary>Every line in the octant: 0 &lt;= t, s &lt;= 1, t &lt;= 1, 0 &lt;= s, anticlockwise.</summary>
        public void SetToSquare()
        {
            _count = 0;
            _largestCoefficient = 0;
            Add(0, -1, 0);
            Add(1, 0, 1);
            Add(0, 1, 1);
            Add(-1, 0, 0);
            SetCorner(0, 1, 0);
            SetCorner(1, 1, 1);
            SetCorner(2, 0, 1);
            SetCorner(3, 0, 0);
        }

        private void SetCorner(int corner, long s, long t)
        {
            _sNumerator[corner] = s;
            _tNumerator[corner] = t;
            _denominator[corner] = 1;
        }

        /// <summary>Makes this view the same as other, corners and all.</summary>
        public void CopyFrom(LineView other)
        {
            _count = 0;
            _largestCoefficient = 0;
            for (var index = 0; index < other._count; index++)
            {
                Add(other._a[index], other._b[index], other._c[index]);
                _sNumerator[index] = other._sNumerator[index];
                _tNumerator[index] = other._tNumerator[index];
                _denominator[index] = other._denominator[index];
            }
        }

        /// <summary>The columns at depth whose band of lines this view's lines fall in: from floor of the smallest s depth + t to one less than the ceiling of the largest.</summary>
        public void ColumnRange(int depth, out int firstColumn, out int lastColumn)
        {
            var lowestNumerator = depth * _sNumerator[0] + _tNumerator[0];
            var lowestDenominator = _denominator[0];
            var highestNumerator = lowestNumerator;
            var highestDenominator = lowestDenominator;
            for (var corner = 1; corner < _count; corner++)
            {
                var numerator = depth * _sNumerator[corner] + _tNumerator[corner];
                var denominator = _denominator[corner];
                if (numerator * lowestDenominator < lowestNumerator * denominator)
                {
                    lowestNumerator = numerator;
                    lowestDenominator = denominator;
                }

                if (numerator * highestDenominator > highestNumerator * denominator)
                {
                    highestNumerator = numerator;
                    highestDenominator = denominator;
                }
            }

            firstColumn = (int)(lowestNumerator / lowestDenominator);
            lastColumn = (int)((highestNumerator + highestDenominator - 1) / highestDenominator) - 1;
        }

        /// <summary>Fills target with this view's lines that also meet a s + b t &lt;= c; false, leaving target without area, when none of positive area do.</summary>
        public bool ClipInto(LineView target, long a, long b, long c)
        {
            var inside = 0;
            Span<bool> isInside = _count <= 64 ? stackalloc bool[_count] : new bool[_count];
            for (var corner = 0; corner < _count; corner++)
            {
                isInside[corner] = a * _sNumerator[corner] + b * _tNumerator[corner] <= c * _denominator[corner];
                inside += isInside[corner] ? 1 : 0;
            }

            target._count = 0;
            target._largestCoefficient = 0;
            if (inside == 0)
            {
                return false;
            }

            if (inside == _count)
            {
                target.CopyFrom(this);
                return true;
            }

            for (var edge = 0; edge < _count; edge++)
            {
                var startInside = isInside[edge == 0 ? _count - 1 : edge - 1];
                var endInside = isInside[edge];
                if (startInside || endInside)
                {
                    target.Add(_a[edge], _b[edge], _c[edge]);
                }

                if (startInside && !endInside)
                {
                    target.Add(a, b, c);
                }
            }

            return target.ComputeCorners() && target.HasArea();
        }

        /// <summary>The slope of the view's centre, roughly: the mean of its corners' slopes.</summary>
        public double CentreSlope()
        {
            var sum = 0.0;
            for (var corner = 0; corner < _count; corner++)
            {
                sum += (double)_sNumerator[corner] / _denominator[corner];
            }

            return sum / _count;
        }

        /// <summary>A line strictly inside the view, as column(i) = floor((rise i + offset) / run): the centre of the first triangle of corners with area.</summary>
        public void InteriorLine(out long rise, out long run, out long offset)
        {
            for (var corner = 1; corner + 1 < _count; corner++)
            {
                if (Cross(0, corner, corner + 1) > 0)
                {
                    var first = _denominator[0];
                    var second = _denominator[corner];
                    var third = _denominator[corner + 1];
                    run = 3 * first * second * third;
                    rise = _sNumerator[0] * second * third + _sNumerator[corner] * first * third + _sNumerator[corner + 1] * first * second;
                    offset = _tNumerator[0] * second * third + _tNumerator[corner] * first * third + _tNumerator[corner + 1] * first * second;
                    return;
                }
            }

            throw new InvalidOperationException("A LineView without area has no line inside it.");
        }

        private void Add(long a, long b, long c)
        {
            if (_count == _a.Length)
            {
                var size = _count * 2;
                Array.Resize(ref _a, size);
                Array.Resize(ref _b, size);
                Array.Resize(ref _c, size);
                Array.Resize(ref _sNumerator, size);
                Array.Resize(ref _tNumerator, size);
                Array.Resize(ref _denominator, size);
            }

            _a[_count] = a;
            _b[_count] = b;
            _c[_count] = c;
            _count++;
            _largestCoefficient = System.Math.Max(_largestCoefficient, System.Math.Max(System.Math.Abs(a), System.Math.Abs(c)));
        }

        /// <summary>Works out each corner, where a constraint meets the next, then drops each constraint whose edge has shrunk to a point (a clip through a corner leaves one) and works them out again, so corners stay distinct; false when two meeting constraints are parallel, which only a view without area has.</summary>
        private bool ComputeCorners()
        {
            if (!ComputeEachCorner())
            {
                return false;
            }

            var kept = 0;
            for (var edge = 0; edge < _count; edge++)
            {
                if (IsSameCorner(edge == 0 ? _count - 1 : edge - 1, edge))
                {
                    continue;
                }

                _a[kept] = _a[edge];
                _b[kept] = _b[edge];
                _c[kept] = _c[edge];
                kept++;
            }

            if (kept == _count)
            {
                return true;
            }

            if (kept < 3)
            {
                _count = 0;
                return false;
            }

            _count = kept;
            return ComputeEachCorner();
        }

        private bool IsSameCorner(int first, int second) =>
            _sNumerator[first] * _denominator[second] == _sNumerator[second] * _denominator[first] &&
            _tNumerator[first] * _denominator[second] == _tNumerator[second] * _denominator[first];

        /// <summary>Each corner, where a constraint meets the next; false when two meeting ones are parallel.</summary>
        private bool ComputeEachCorner()
        {
            for (var corner = 0; corner < _count; corner++)
            {
                var next = corner + 1 == _count ? 0 : corner + 1;
                var determinant = _a[corner] * _b[next] - _a[next] * _b[corner];
                if (determinant == 0)
                {
                    _count = 0;
                    return false;
                }

                var sNumerator = _c[corner] * _b[next] - _c[next] * _b[corner];
                var tNumerator = _a[corner] * _c[next] - _a[next] * _c[corner];
                if (determinant < 0)
                {
                    determinant = -determinant;
                    sNumerator = -sNumerator;
                    tNumerator = -tNumerator;
                }

                _sNumerator[corner] = sNumerator;
                _tNumerator[corner] = tNumerator;
                _denominator[corner] = determinant;
            }

            return true;
        }

        /// <summary>Whether the view has positive area: some triangle of the fan from the first corner turns anticlockwise.</summary>
        private bool HasArea()
        {
            for (var corner = 1; corner + 1 < _count; corner++)
            {
                if (Cross(0, corner, corner + 1) > 0)
                {
                    return true;
                }
            }

            _count = 0;
            return false;
        }

        /// <summary>The sign of the turn from corner first through second to third, exactly.</summary>
        /// <remarks>
        /// A corner's numerators are at most twice the largest coefficient, or its square, over a denominator no larger, so
        /// the turn fits a long while every coefficient is at most NarrowCoefficientLimit (about 16 x 2048^5), and is worked
        /// out in 128 bits past it.
        /// </remarks>
        private int Cross(int first, int second, int third)
        {
            if (_largestCoefficient <= NarrowCoefficientLimit)
            {
                var narrowFirstStepS = _sNumerator[second] * _denominator[first] - _sNumerator[first] * _denominator[second];
                var narrowFirstStepT = _tNumerator[second] * _denominator[first] - _tNumerator[first] * _denominator[second];
                var narrowSecondStepS = _sNumerator[third] * _denominator[second] - _sNumerator[second] * _denominator[third];
                var narrowSecondStepT = _tNumerator[third] * _denominator[second] - _tNumerator[second] * _denominator[third];
                return System.Math.Sign(narrowFirstStepS * narrowSecondStepT - narrowFirstStepT * narrowSecondStepS);
            }

            return Int128.Sign(WideCross(first, second, third));
        }

        private Int128 WideCross(int first, int second, int third)
        {
            Int128 firstStepS = (Int128)_sNumerator[second] * _denominator[first] - (Int128)_sNumerator[first] * _denominator[second];
            Int128 firstStepT = (Int128)_tNumerator[second] * _denominator[first] - (Int128)_tNumerator[first] * _denominator[second];
            Int128 secondStepS = (Int128)_sNumerator[third] * _denominator[second] - (Int128)_sNumerator[second] * _denominator[third];
            Int128 secondStepT = (Int128)_tNumerator[third] * _denominator[second] - (Int128)_tNumerator[second] * _denominator[third];
            return firstStepS * secondStepT - firstStepT * secondStepS;
        }
    }
}
