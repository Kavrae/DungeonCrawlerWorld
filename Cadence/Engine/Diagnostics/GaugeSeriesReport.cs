using System.Text.Json;
using System.Text.Json.Serialization;
using static Engine.Diagnostics.DiagnosticsReportRounding;

namespace Engine.Diagnostics;

/// <summary>Keeps every gauge's per-frame value across a benchmark range, beside the frame's EcsContext.Update cost, then writes one report.</summary>
/// <remarks>
/// Every series is allocated at construction, before the range opens, so recording allocates nothing:
/// an allocation inside the range would show up in the Process gauges being recorded.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class GaugeSeriesReport
{
    private const string FrameGroupName = "Frame";
    private const string UpdateMillisecondsGaugeName = "UpdateMilliseconds";

    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly GaugeTracker _tracker;
    private readonly double[] _updateMillisecondsSeries;
    private readonly double[][] _gaugeSeries;

    public GaugeSeriesReport(GaugeTracker tracker, BenchmarkFrameRange range)
    {
        _tracker = tracker;
        Range = range;
        _updateMillisecondsSeries = new double[range.FrameCount];
        _gaugeSeries = new double[tracker.Gauges.Count][];
        for (var gaugeIndex = 0; gaugeIndex < _gaugeSeries.Length; gaugeIndex++)
        {
            _gaugeSeries[gaugeIndex] = new double[range.FrameCount];
        }
    }

    public BenchmarkFrameRange Range { get; }

    /// <summary>How many of the range's frames have been recorded so far.</summary>
    public long RecordedFrameCount { get; private set; }

    /// <summary>True once the range's last frame, EndFrame - 1, has been recorded.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Records the tracker's values for frameCount, which it has just sampled, if frameCount is inside the range.</summary>
    public void Record(long frameCount, TimeSpan updateElapsed)
    {
        if (IsComplete || frameCount < Range.StartFrame || frameCount >= Range.EndFrame)
        {
            return;
        }

        var frameIndex = frameCount - Range.StartFrame;
        _updateMillisecondsSeries[frameIndex] = updateElapsed.TotalMilliseconds;
        for (var gaugeIndex = 0; gaugeIndex < _gaugeSeries.Length; gaugeIndex++)
        {
            _gaugeSeries[gaugeIndex][frameIndex] = _tracker.GetLatestValue(gaugeIndex);
        }

        RecordedFrameCount++;
        IsComplete = frameCount == Range.EndFrame - 1;
    }

    /// <summary>Writes gauges-&lt;timestamp&gt;-&lt;pid&gt;.json and .txt to outputDirectory and returns the json path.</summary>
    /// <param name="randomSeed">The seed of the session measured -- two reports are only comparable when it matches.</param>
    public string WriteReport(string outputDirectory, int? randomSeed)
    {
        var items = new List<GaugeSeriesItem>(_gaugeSeries.Length + 1)
        {
            CreateItem(FrameGroupName, UpdateMillisecondsGaugeName, GaugeKind.Level, _updateMillisecondsSeries),
        };

        for (var gaugeIndex = 0; gaugeIndex < _gaugeSeries.Length; gaugeIndex++)
        {
            var gauge = _tracker.Gauges[gaugeIndex];
            items.Add(CreateItem(gauge.GroupName, gauge.GaugeName, gauge.Kind, _gaugeSeries[gaugeIndex]));
        }

        var processId = Environment.ProcessId;
        var report = new GaugeReport(DateTime.UtcNow, randomSeed, processId, Range.StartFrame, Range.EndFrame, Range.FrameCount, RecordedFrameCount, items);

        Directory.CreateDirectory(outputDirectory);
        var stem = Path.Combine(outputDirectory, $"gauges-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{processId}");
        File.WriteAllText(stem + ".json", JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllLines(stem + ".txt", FormatText(report));
        return stem + ".json";
    }

    private GaugeSeriesItem CreateItem(string groupName, string gaugeName, GaugeKind kind, double[] series)
    {
        var minimum = double.MaxValue;
        var maximum = double.MinValue;
        var maximumFrame = Range.StartFrame;
        var total = 0d;
        var framesNonzero = 0;
        for (var frameIndex = 0; frameIndex < series.Length; frameIndex++)
        {
            var value = series[frameIndex];
            minimum = System.Math.Min(minimum, value);
            if (value > maximum)
            {
                maximum = value;
                maximumFrame = Range.StartFrame + frameIndex;
            }

            total += value;
            if (value != 0)
            {
                framesNonzero++;
            }
        }

        var roundedSeries = new double[series.Length];
        for (var frameIndex = 0; frameIndex < series.Length; frameIndex++)
        {
            roundedSeries[frameIndex] = RoundForReport(series[frameIndex]);
        }

        return kind is GaugeKind.Cumulative
            ? new GaugeSeriesItem(groupName, gaugeName, kind.ToString(), null, null, null, RoundForReport(total), RoundForReport(maximum), maximumFrame, framesNonzero, roundedSeries)
            : new GaugeSeriesItem(groupName, gaugeName, kind.ToString(), RoundForReport(minimum), RoundForReport(maximum), RoundForReport(total / series.Length), null, null, maximumFrame, null, roundedSeries);
    }

    private static IEnumerable<string> FormatText(GaugeReport report)
    {
        yield return $"[Gauges] seed {report.RandomSeed}, frames {report.StartFrame}-{report.EndFrame}, {report.RecordedFrameCount:N0} of {report.FrameCount:N0} recorded";
        foreach (var item in report.Gauges)
        {
            yield return item.Total is { } total
                ? $"  {item.Group}/{item.Name}: total {FormatForReport(total)}, max {FormatForReport(item.MaximumPerFrame)}/frame at frame {item.MaximumFrame}, {item.FramesNonzero:N0} frames nonzero"
                : $"  {item.Group}/{item.Name}: mean {FormatForReport(item.Mean)} (min {FormatForReport(item.Minimum)}, max {FormatForReport(item.Maximum)} at frame {item.MaximumFrame})";
        }
    }

    private sealed record GaugeReport(
        DateTime TimestampUtc,
        int? RandomSeed,
        int ProcessId,
        long StartFrame,
        long EndFrame,
        long FrameCount,
        long RecordedFrameCount,
        List<GaugeSeriesItem> Gauges);

    /// <param name="MaximumFrame">The first frame holding Maximum (a level) or MaximumPerFrame (a cumulative gauge).</param>
    /// <param name="Series">One value per frame of the range, StartFrame first: a level as read, a cumulative gauge's change that frame.</param>
    private sealed record GaugeSeriesItem(
        string Group,
        string Name,
        string Kind,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Minimum,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Maximum,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Mean,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Total,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? MaximumPerFrame,
        long MaximumFrame,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? FramesNonzero,
        double[] Series);
}
