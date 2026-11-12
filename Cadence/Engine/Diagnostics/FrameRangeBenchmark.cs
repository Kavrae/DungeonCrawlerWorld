using System.Diagnostics;
using System.Text.Json;

namespace Engine.Diagnostics;

/// <summary>Totals every recorded frame cost across a fixed range of simulation frames, then writes one report.</summary>
/// <remarks>
/// Exists because FrameBudgetTracker's rolling one-second wall-clock window can't give two runs
/// the same workload: frame cost depends on what the world is doing, and startup time varies,
/// so the same wall-clock sample lands on different simulation frames each run. Two same-seed
/// runs of the same code differed by up to 57% per system that way. Keying the window to
/// simulation frame numbers instead means a seeded run measures exactly the same frames of
/// exactly the same simulation every time.
///
/// DiagnosticsEngine drives it from the simulation frame hooks. Recording opens when frame
/// StartFrame starts and closes when frame EndFrame - 1 ends, so it covers Update for frames
/// [StartFrame, EndFrame) plus every Draw and shell Update between them. Update costs are the deterministic part. Draw costs per simulation frame still
/// depend on frame pacing -- a game falling behind runs several Updates per Draw -- which is
/// what WallClockMilliseconds in the report is for: well above FrameCount / 60 seconds means
/// the run fell behind.
///
/// It tracks every measurement's nesting from the start, so a row open inside itself adds only its outermost
/// measurement's time, and grows its row storage for every row it sees before the range opens, so nothing inside the
/// range grows an array. An entry never recorded inside the range is left out of the report; each row's record count is
/// written beside its time, so a row that got slower reads apart from one that ran more often. After the range, Begin
/// and Record cost one bool check, and DiagnosticsEngine stops listening as soon as nothing else needs the channel.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class FrameRangeBenchmark(BenchmarkFrameRange range) : IFrameCostRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly FrameCostOpenMeasurements _openMeasurements = new();
    private RowTotals[] _totalsByRow = new RowTotals[64];
    private int _rowCountSeen;
    private long _recordingStartTimestamp;
    private TimeSpan _wallClockElapsed;

    public BenchmarkFrameRange Range { get; } = range;

    /// <summary>True while the current simulation frame is inside Range.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>True once frame EndFrame - 1 has ended -- the totals are final.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Opens recording when a frame inside the range starts.</summary>
    public void SimulationFrameStarting(long frameCount)
    {
        if (IsComplete || IsRecording || frameCount < Range.StartFrame || frameCount >= Range.EndFrame)
        {
            return;
        }

        IsRecording = true;
        _recordingStartTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>Closes recording once the range's last frame, EndFrame - 1, has ended.</summary>
    public void SimulationFrameEnded(long frameCount)
    {
        if (!IsRecording || frameCount < Range.EndFrame - 1)
        {
            return;
        }

        IsRecording = false;
        IsComplete = true;
        _wallClockElapsed = Stopwatch.GetElapsedTime(_recordingStartTimestamp);
    }

    public void Begin(FrameCostRowId row)
    {
        if (IsComplete)
        {
            return;
        }

        EnsureRow(row.Index);
        _openMeasurements.Open(row.Index);
    }

    public void Record(FrameCostRowId row, TimeSpan elapsed)
    {
        if (IsComplete)
        {
            return;
        }

        var rowIndex = row.Index;
        EnsureRow(rowIndex);
        var isOutermost = _openMeasurements.Close(rowIndex);
        if (!IsRecording)
        {
            return;
        }

        ref var totals = ref _totalsByRow[rowIndex];
        totals.RecordCount++;
        if (!isOutermost)
        {
            return;
        }

        var milliseconds = elapsed.TotalMilliseconds;
        totals.TotalMilliseconds += milliseconds;
        totals.WorstMilliseconds = System.Math.Max(totals.WorstMilliseconds, milliseconds);
    }

    /// <summary>Total milliseconds recorded for one entry so far; 0 if it never recorded inside the range.</summary>
    public double GetTotalMilliseconds(FrameCostCategory category, string groupName, string itemName) =>
        TotalsOf(category, groupName, itemName).TotalMilliseconds;

    /// <summary>How many times one entry was recorded inside the range so far, nested measurements of itself included.</summary>
    public int GetRecordCount(FrameCostCategory category, string groupName, string itemName) =>
        TotalsOf(category, groupName, itemName).RecordCount;

    /// <summary>Writes benchmark-&lt;timestamp&gt;-&lt;pid&gt;.json to outputDirectory and returns its path.</summary>
    /// <param name="randomSeed">The seed of the session measured -- two reports are only comparable when it matches.</param>
    /// <param name="conditions">What the run measured under -- two reports are only comparable when they match too.</param>
    public string WriteReport(string outputDirectory, int? randomSeed, DiagnosticsRunConditions conditions)
    {
        Directory.CreateDirectory(outputDirectory);

        var processId = Environment.ProcessId;
        var report = new DiagnosticsBenchmarkReport(
            DateTime.UtcNow,
            randomSeed,
            processId,
            Range.StartFrame,
            Range.EndFrame,
            Range.FrameCount,
            _wallClockElapsed.TotalMilliseconds,
            GroupByCategory(FrameCostCategory.Update),
            GroupByCategory(FrameCostCategory.Draw),
            conditions);

        var path = Path.Combine(outputDirectory, $"benchmark-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{processId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
        return path;
    }

    private RowTotals TotalsOf(FrameCostCategory category, string groupName, string itemName) =>
        FrameCostRowIds.TryFind(category, groupName, itemName, out var row) && row.Index < _rowCountSeen ? _totalsByRow[row.Index] : default;

    private void EnsureRow(int rowIndex)
    {
        if (rowIndex < _rowCountSeen)
        {
            return;
        }

        _rowCountSeen = rowIndex + 1;
        if (rowIndex >= _totalsByRow.Length)
        {
            Array.Resize(ref _totalsByRow, System.Math.Max(rowIndex + 1, _totalsByRow.Length * 2));
        }
    }

    private Dictionary<string, IReadOnlyList<DiagnosticsBenchmarkItem>> GroupByCategory(FrameCostCategory category)
    {
        var groups = new Dictionary<string, List<DiagnosticsBenchmarkItem>>();
        for (var rowIndex = 0; rowIndex < _rowCountSeen; rowIndex++)
        {
            var (entryCategory, groupName, itemName) = FrameCostRowIds.NameOf(new FrameCostRowId(rowIndex));
            var totals = _totalsByRow[rowIndex];
            if (entryCategory != category || totals.RecordCount == 0)
            {
                continue;
            }

            if (!groups.TryGetValue(groupName, out var items))
            {
                items = [];
                groups[groupName] = items;
            }

            items.Add(new DiagnosticsBenchmarkItem(itemName, totals.TotalMilliseconds, totals.TotalMilliseconds / Range.FrameCount, totals.WorstMilliseconds, totals.RecordCount));
        }

        foreach (var items in groups.Values)
        {
            items.Sort(static (first, second) => second.TotalMilliseconds.CompareTo(first.TotalMilliseconds));
        }

        return groups.ToDictionary(static group => group.Key, static group => (IReadOnlyList<DiagnosticsBenchmarkItem>)group.Value);
    }

    private struct RowTotals
    {
        public double TotalMilliseconds;
        public double WorstMilliseconds;
        public int RecordCount;
    }
}
