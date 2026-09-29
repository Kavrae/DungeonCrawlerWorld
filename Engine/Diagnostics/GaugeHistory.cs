namespace Engine.Diagnostics;

/// <summary>The last HistoryFrameCount simulation frames of every gauge, beside each frame's EcsContext.Update cost, for a live display.</summary>
/// <remarks>
/// Row 0 is Frame/UpdateMilliseconds; row i + 1 is the tracker's gauge i. A ring per row, allocated
/// at construction, so recording allocates nothing. Readers copy a row out oldest-first.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class GaugeHistory
{
    /// <summary>How many frames each row keeps: five seconds at 60 frames per second.</summary>
    public const int HistoryFrameCount = 300;

    private readonly GaugeTracker _tracker;
    private readonly Gauge[] _rows;
    private readonly double[][] _ringsByRow;
    private int _nextRingIndex;

    public GaugeHistory(GaugeTracker tracker)
    {
        _tracker = tracker;
        _rows = [new Gauge("Frame", "UpdateMilliseconds", GaugeKind.Level, static () => 0), .. tracker.Gauges];
        _ringsByRow = new double[_rows.Length][];
        for (var rowIndex = 0; rowIndex < _rows.Length; rowIndex++)
        {
            _ringsByRow[rowIndex] = new double[HistoryFrameCount];
        }
    }

    /// <summary>Every row's gauge, Frame/UpdateMilliseconds first; only its names and kind are meaningful here.</summary>
    public IReadOnlyList<Gauge> Rows => _rows;

    /// <summary>How many frames are held, up to HistoryFrameCount.</summary>
    public int RecordedFrameCount { get; private set; }

    /// <summary>The simulation frame last recorded; 0 before any.</summary>
    public long LatestFrame { get; private set; }

    /// <summary>Records the tracker's values for frameCount, which it has just sampled.</summary>
    public void Record(long frameCount, TimeSpan updateElapsed)
    {
        _ringsByRow[0][_nextRingIndex] = updateElapsed.TotalMilliseconds;
        for (var rowIndex = 1; rowIndex < _rows.Length; rowIndex++)
        {
            _ringsByRow[rowIndex][_nextRingIndex] = _tracker.GetLatestValue(rowIndex - 1);
        }

        _nextRingIndex = (_nextRingIndex + 1) % HistoryFrameCount;
        RecordedFrameCount = System.Math.Min(RecordedFrameCount + 1, HistoryFrameCount);
        LatestFrame = frameCount;
    }

    /// <summary>Row rowIndex's value on the latest recorded frame; 0 before any.</summary>
    public double GetLatestValue(int rowIndex) =>
        RecordedFrameCount == 0 ? 0 : _ringsByRow[rowIndex][(_nextRingIndex + HistoryFrameCount - 1) % HistoryFrameCount];

    /// <summary>Copies row rowIndex's recorded values into destination, oldest first, and returns how many it copied.</summary>
    /// <remarks>destination must hold at least RecordedFrameCount values.</remarks>
    public int CopyHistory(int rowIndex, Span<double> destination)
    {
        var ring = _ringsByRow[rowIndex];
        var oldestRingIndex = RecordedFrameCount < HistoryFrameCount ? 0 : _nextRingIndex;
        var newerPart = ring.AsSpan(oldestRingIndex, RecordedFrameCount < HistoryFrameCount ? RecordedFrameCount : HistoryFrameCount - oldestRingIndex);
        newerPart.CopyTo(destination);
        if (RecordedFrameCount == HistoryFrameCount && oldestRingIndex > 0)
        {
            ring.AsSpan(0, oldestRingIndex).CopyTo(destination[newerPart.Length..]);
        }

        return RecordedFrameCount;
    }
}
