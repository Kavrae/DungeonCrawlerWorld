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
/// BeginSimulationFrame(frameCount) is called once per simulation frame, before that frame's
/// Update. Recording opens when frame StartFrame begins and closes when frame EndFrame begins,
/// so it covers Update for frames [StartFrame, EndFrame) plus every Draw and shell Update in
/// between. Update costs are the deterministic part. Draw costs per simulation frame still
/// depend on frame pacing -- a game falling behind runs several Updates per Draw -- which is
/// what WallClockMilliseconds in the report is for: well above FrameCount / 60 seconds means
/// the run fell behind.
///
/// Records outside the range cost one bool check.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class FrameRangeBenchmark : IFrameCostRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Dictionary<(FrameCostCategory Category, string GroupName, string ItemName), double> _totalMilliseconds = [];
    private long _recordingStartTimestamp;
    private TimeSpan _wallClockElapsed;

    public FrameRangeBenchmark(BenchmarkFrameRange range)
    {
        Range = range;
    }

    public BenchmarkFrameRange Range { get; }

    /// <summary>True while the current simulation frame is inside Range.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>True once frame EndFrame has begun -- the totals are final.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Opens recording when frameCount reaches StartFrame and closes it at EndFrame.</summary>
    public void BeginSimulationFrame(long frameCount)
    {
        if (IsComplete)
        {
            return;
        }

        if (!IsRecording && frameCount >= Range.StartFrame && frameCount < Range.EndFrame)
        {
            IsRecording = true;
            _recordingStartTimestamp = Stopwatch.GetTimestamp();
        }
        else if (IsRecording && frameCount >= Range.EndFrame)
        {
            IsRecording = false;
            IsComplete = true;
            _wallClockElapsed = Stopwatch.GetElapsedTime(_recordingStartTimestamp);
        }
    }

    public void Record(FrameCostCategory category, string groupName, string itemName, TimeSpan elapsed)
    {
        if (!IsRecording)
        {
            return;
        }

        var key = (category, groupName, itemName);
        _totalMilliseconds[key] = _totalMilliseconds.GetValueOrDefault(key) + elapsed.TotalMilliseconds;
    }

    /// <summary>Total milliseconds recorded for one entry so far; 0 if it never recorded inside the range.</summary>
    public double GetTotalMilliseconds(FrameCostCategory category, string groupName, string itemName) =>
        _totalMilliseconds.GetValueOrDefault((category, groupName, itemName));

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
        foreach (var ((entryCategory, groupName, itemName), totalMilliseconds) in _totalMilliseconds)
        {
            if (entryCategory != category)
            {
                continue;
            }

            if (!groups.TryGetValue(groupName, out var items))
            {
                items = [];
                groups[groupName] = items;
            }

            items.Add(new BenchmarkItem(itemName, totalMilliseconds, totalMilliseconds / Range.FrameCount));
        }

        foreach (var items in groups.Values)
        {
            items.Sort(static (a, b) => b.TotalMilliseconds.CompareTo(a.TotalMilliseconds));
        }

        return groups;
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

    private sealed record BenchmarkItem(string Name, double TotalMilliseconds, double MillisecondsPerFrame);
}
