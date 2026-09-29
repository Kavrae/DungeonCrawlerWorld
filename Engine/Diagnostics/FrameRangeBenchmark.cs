using System.Diagnostics;
using System.Runtime.InteropServices;
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
/// Before the range opens, Record still adds each entry it sees, with nothing recorded, so the first
/// frame inside the range runs no code for the first time (JIT) and grows no dictionary -- either
/// would land in that frame's cost. An entry never recorded inside the range is left out of the
/// report. After the range, Record costs one bool check.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class FrameRangeBenchmark : IFrameCostRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Dictionary<(FrameCostCategory Category, string GroupName, string ItemName), EntryTotals> _entryTotals = [];
    private long _recordingStartTimestamp;
    private TimeSpan _wallClockElapsed;

    public FrameRangeBenchmark(BenchmarkFrameRange range)
    {
        Range = range;
    }

    public BenchmarkFrameRange Range { get; }

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

    public void Record(FrameCostCategory category, string groupName, string itemName, TimeSpan elapsed)
    {
        if (IsComplete)
        {
            return;
        }

        ref var entryTotals = ref CollectionsMarshal.GetValueRefOrAddDefault(_entryTotals, (category, groupName, itemName), out _);
        if (!IsRecording)
        {
            return;
        }

        entryTotals.TotalMilliseconds += elapsed.TotalMilliseconds;
        entryTotals.WorstMilliseconds = System.Math.Max(entryTotals.WorstMilliseconds, elapsed.TotalMilliseconds);
        entryTotals.RecordCount++;
    }

    /// <summary>Total milliseconds recorded for one entry so far; 0 if it never recorded inside the range.</summary>
    public double GetTotalMilliseconds(FrameCostCategory category, string groupName, string itemName) =>
        _entryTotals.GetValueOrDefault((category, groupName, itemName)).TotalMilliseconds;

    /// <summary>Writes benchmark-&lt;timestamp&gt;-&lt;pid&gt;.json to outputDirectory and returns its path.</summary>
    /// <param name="randomSeed">The seed of the session measured -- two reports are only comparable when it matches.</param>
    public string WriteReport(string outputDirectory, int? randomSeed)
    {
        Directory.CreateDirectory(outputDirectory);

        var processId = Environment.ProcessId;
        var report = new BenchmarkReport(
            DateTime.UtcNow,
            randomSeed,
            processId,
            Range.StartFrame,
            Range.EndFrame,
            Range.FrameCount,
            _wallClockElapsed.TotalMilliseconds,
            GroupByCategory(FrameCostCategory.Update),
            GroupByCategory(FrameCostCategory.Draw));

        var path = Path.Combine(outputDirectory, $"benchmark-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{processId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
        return path;
    }

    private Dictionary<string, List<BenchmarkItem>> GroupByCategory(FrameCostCategory category)
    {
        var groups = new Dictionary<string, List<BenchmarkItem>>();
        foreach (var ((entryCategory, groupName, itemName), entryTotals) in _entryTotals)
        {
            if (entryCategory != category || entryTotals.RecordCount == 0)
            {
                continue;
            }

            if (!groups.TryGetValue(groupName, out var items))
            {
                items = [];
                groups[groupName] = items;
            }

            items.Add(new BenchmarkItem(itemName, entryTotals.TotalMilliseconds, entryTotals.TotalMilliseconds / Range.FrameCount, entryTotals.WorstMilliseconds));
        }

        foreach (var items in groups.Values)
        {
            items.Sort(static (a, b) => b.TotalMilliseconds.CompareTo(a.TotalMilliseconds));
        }

        return groups;
    }

    private struct EntryTotals
    {
        public double TotalMilliseconds;
        public double WorstMilliseconds;
        public int RecordCount;
    }

    private sealed record BenchmarkReport(
        DateTime TimestampUtc,
        int? RandomSeed,
        int ProcessId,
        long StartFrame,
        long EndFrame,
        long FrameCount,
        double WallClockMilliseconds,
        Dictionary<string, List<BenchmarkItem>> Update,
        Dictionary<string, List<BenchmarkItem>> Draw);

    /// <param name="WorstMilliseconds">The largest single recording -- one frame's cost for a system or EcsContext.Update, one dispatch for an event.</param>
    private sealed record BenchmarkItem(string Name, double TotalMilliseconds, double MillisecondsPerFrame, double WorstMilliseconds);
}
