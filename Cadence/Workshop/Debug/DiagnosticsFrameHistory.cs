namespace Workshop.Debug;

/// <summary>The last Capacity frame records of a connected game: each frame's number, its update milliseconds and every gauge's value.</summary>
/// <remarks>
/// Series 0 is the update milliseconds; series i + 1 is the session's gauge i, as GaugeHistory numbers its rows. A ring
/// per series, allocated at construction, so recording allocates nothing. Readers copy a series out oldest-first.
/// </remarks>
public sealed class DiagnosticsFrameHistory
{
    /// <summary>Ten minutes at 60 frames per second.</summary>
    public const int DefaultCapacity = 10 * 60 * 60;

    private readonly long[] _frames;
    private readonly double[][] _ringsBySeries;
    private int _nextRingIndex;

    public DiagnosticsFrameHistory(int gaugeCount, int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        Capacity = capacity;
        _frames = new long[capacity];
        _ringsBySeries = new double[gaugeCount + 1][];
        for (var seriesIndex = 0; seriesIndex < _ringsBySeries.Length; seriesIndex++)
        {
            _ringsBySeries[seriesIndex] = new double[capacity];
        }
    }

    public int Capacity { get; }

    /// <summary>The update milliseconds' series and one per gauge.</summary>
    public int SeriesCount => _ringsBySeries.Length;

    /// <summary>How many frames are held, up to Capacity.</summary>
    public int RecordedFrameCount { get; private set; }

    /// <summary>The frame last recorded; 0 before any.</summary>
    public long LatestFrame { get; private set; }

    /// <summary>Records frame's update milliseconds and gaugeValues, one per gauge; values past the gauges held are ignored and missing ones read 0.</summary>
    public void Record(long frame, double updateMilliseconds, ReadOnlySpan<double> gaugeValues)
    {
        _frames[_nextRingIndex] = frame;
        _ringsBySeries[0][_nextRingIndex] = updateMilliseconds;
        for (var seriesIndex = 1; seriesIndex < _ringsBySeries.Length; seriesIndex++)
        {
            _ringsBySeries[seriesIndex][_nextRingIndex] = seriesIndex - 1 < gaugeValues.Length ? gaugeValues[seriesIndex - 1] : 0;
        }

        _nextRingIndex = (_nextRingIndex + 1) % Capacity;
        RecordedFrameCount = System.Math.Min(RecordedFrameCount + 1, Capacity);
        TotalRecordedCount++;
        LatestFrame = frame;
    }

    /// <summary>Every frame ever recorded, held or not: record r, for r from TotalRecordedCount - RecordedFrameCount up, is still held.</summary>
    public long TotalRecordedCount { get; private set; }

    /// <summary>The first record still held.</summary>
    public long OldestHeldRecord => TotalRecordedCount - RecordedFrameCount;

    /// <summary>Copies series seriesIndex's values for records [endRecord - count, endRecord), oldest first, clamped to the records held, and returns how many it copied.</summary>
    public int CopyWindow(int seriesIndex, long endRecord, int count, Span<double> destination) =>
        CopyRecords(_ringsBySeries[seriesIndex], endRecord, count, destination);

    /// <summary>Copies the frame numbers of records [endRecord - count, endRecord), oldest first, clamped to the records held, and returns how many it copied.</summary>
    public int CopyFrameWindow(long endRecord, int count, Span<long> destination) => CopyRecords(_frames, endRecord, count, destination);

    private int CopyRecords<T>(T[] ring, long endRecord, int count, Span<T> destination)
    {
        var end = System.Math.Min(endRecord, TotalRecordedCount);
        var start = System.Math.Max(end - count, OldestHeldRecord);
        if (end <= start)
        {
            return 0;
        }

        var copied = (int)(end - start);
        var startRingIndex = (int)(start % Capacity);
        var firstPart = System.Math.Min(copied, Capacity - startRingIndex);
        ring.AsSpan(startRingIndex, firstPart).CopyTo(destination);
        if (firstPart < copied)
        {
            ring.AsSpan(0, copied - firstPart).CopyTo(destination[firstPart..]);
        }

        return copied;
    }

    /// <summary>Series seriesIndex's value on the latest recorded frame; 0 before any.</summary>
    public double GetLatestValue(int seriesIndex) =>
        RecordedFrameCount == 0 ? 0 : _ringsBySeries[seriesIndex][(_nextRingIndex + Capacity - 1) % Capacity];

    /// <summary>Copies series seriesIndex's recorded values into destination, oldest first, and returns how many it copied.</summary>
    /// <remarks>destination must hold at least RecordedFrameCount values.</remarks>
    public int CopySeries(int seriesIndex, Span<double> destination) => CopyOldestFirst(_ringsBySeries[seriesIndex], destination);

    /// <summary>Copies the recorded frames' numbers into destination, oldest first, and returns how many it copied.</summary>
    public int CopyFrames(Span<long> destination) => CopyOldestFirst(_frames, destination);

    private int CopyOldestFirst<T>(T[] ring, Span<T> destination)
    {
        var oldestRingIndex = RecordedFrameCount < Capacity ? 0 : _nextRingIndex;
        var olderPart = ring.AsSpan(oldestRingIndex, RecordedFrameCount < Capacity ? RecordedFrameCount : Capacity - oldestRingIndex);
        olderPart.CopyTo(destination);
        if (RecordedFrameCount == Capacity && oldestRingIndex > 0)
        {
            ring.AsSpan(0, oldestRingIndex).CopyTo(destination[olderPart.Length..]);
        }

        return RecordedFrameCount;
    }
}
