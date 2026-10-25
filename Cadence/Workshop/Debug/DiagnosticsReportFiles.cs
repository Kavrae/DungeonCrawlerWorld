using System.Text.Json;
using Engine.Diagnostics;

namespace Workshop.Debug;

/// <summary>Reads a report file the Reports browser lists into a recorded session the Live views show and Compare takes -- or, an A/B, into the two sessions it compared.</summary>
/// <remarks>The game's reports are read through DiagnosticsReportReader; the phase-performance-testing skill's saved runs and A/Bs are read here, by the "Category.Group.Item" keys both use.</remarks>
public static class DiagnosticsReportFiles
{
    /// <summary>The session that shows entry in the Live views; null for a kind that opens as a comparison (an A/B).</summary>
    /// <exception cref="InvalidDataException">The file is not the kind its name says.</exception>
    /// <exception cref="IOException">The file couldn't be read.</exception>
    public static LiveDiagnosticsSession? OpenSession(DiagnosticsReportEntry entry)
    {
        var json = DiagnosticsReportCatalog.ReadJson(entry.FilePath);
        switch (entry.Kind)
        {
            case DiagnosticsReportKind.Periodic:
            {
                var report = DiagnosticsReportReader.ReadPeriodicReport(json.Span);
                return Recorded(entry, report.RandomSeed, report.Features, periodicReport: report);
            }

            case DiagnosticsReportKind.Benchmark:
            {
                var report = DiagnosticsReportReader.ReadBenchmarkReport(json.Span);
                return Recorded(entry, report.RandomSeed, nameof(DiagnosticsFeatures.FrameBudget), report.ProcessId, PerFrameReport(report.TimestampUtc, report.RandomSeed, DiagnosticsReportReader.MillisecondsPerFrameOf(report)), frameCostsArePerFrame: true);
            }

            case DiagnosticsReportKind.SavedRun:
            {
                var perFrame = ReadSavedRun(entry, json);
                return Recorded(entry, entry.Seed, nameof(DiagnosticsFeatures.FrameBudget), periodicReport: PerFrameReport(entry.TimestampUtc, entry.Seed, perFrame), frameCostsArePerFrame: true);
            }

            case DiagnosticsReportKind.Memory:
            {
                var report = DiagnosticsReportReader.ReadMemoryReport(json.Span);
                var memory = report.Pools.Select(static pool => new DiagnosticsReportMemoryItem(pool.ComponentType, pool.Count, pool.EstimatedBytes)).ToList();
                return Recorded(entry, report.RandomSeed, nameof(DiagnosticsFeatures.Memory), report.ProcessId, new DiagnosticsReport(report.TimestampUtc, nameof(DiagnosticsFeatures.Memory), report.RandomSeed, null, memory, null, null));
            }

            case DiagnosticsReportKind.Startup:
                return Recorded(entry, null, nameof(DiagnosticsFeatures.Startup), startupReport: DiagnosticsReportReader.ReadStartupReport(json.Span));

            case DiagnosticsReportKind.Gauges:
                return OpenGaugeSeries(entry, DiagnosticsReportReader.ReadGaugeSeriesReport(json.Span));

            default:
                return null;
        }
    }

    /// <summary>An A/B's two sides, as the skill measured them, each a recorded session of frame costs per frame: the baseline build's medians as previous, the current build's as current.</summary>
    /// <exception cref="InvalidDataException">The file is not an A/B.</exception>
    /// <exception cref="IOException">The file couldn't be read.</exception>
    public static (LiveDiagnosticsSession Previous, LiveDiagnosticsSession Current) OpenAbComparison(DiagnosticsReportEntry entry)
    {
        using var document = Parse(DiagnosticsReportCatalog.ReadJson(entry.FilePath), entry);
        var root = document.RootElement;
        if (DiagnosticsReportCatalog.Field(root, "Systems") is not { ValueKind: JsonValueKind.Object } systems)
        {
            throw new InvalidDataException($"{entry.FileName} holds no systems.");
        }

        var previous = new Dictionary<string, double>();
        var current = new Dictionary<string, double>();
        foreach (var system in systems.EnumerateObject())
        {
            if (DiagnosticsReportCatalog.Field(system.Value, "A") is { ValueKind: JsonValueKind.Number } a)
            {
                previous[system.Name] = a.GetDouble();
            }

            if (DiagnosticsReportCatalog.Field(system.Value, "B") is { ValueKind: JsonValueKind.Number } b)
            {
                current[system.Name] = b.GetDouble();
            }
        }

        return (
            Recorded(entry, entry.Seed, nameof(DiagnosticsFeatures.FrameBudget), periodicReport: PerFrameReport(entry.TimestampUtc, entry.Seed, previous), frameCostsArePerFrame: true),
            Recorded(entry, entry.Seed, nameof(DiagnosticsFeatures.FrameBudget), periodicReport: PerFrameReport(entry.TimestampUtc, entry.Seed, current), frameCostsArePerFrame: true));
    }

    /// <summary>A saved run's medians, milliseconds per frame by "Category.Group.Item".</summary>
    private static Dictionary<string, double> ReadSavedRun(DiagnosticsReportEntry entry, ReadOnlyMemory<byte> json)
    {
        using var document = Parse(json, entry);
        if (DiagnosticsReportCatalog.Field(document.RootElement, "Systems") is not { ValueKind: JsonValueKind.Object } systems)
        {
            throw new InvalidDataException($"{entry.FileName} holds no systems.");
        }

        var perFrame = new Dictionary<string, double>();
        foreach (var system in systems.EnumerateObject())
        {
            if (system.Value.ValueKind == JsonValueKind.Number)
            {
                perFrame[system.Name] = system.Value.GetDouble();
            }
        }

        return perFrame;
    }

    private static JsonDocument Parse(ReadOnlyMemory<byte> json, DiagnosticsReportEntry entry)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{entry.FileName} is not valid JSON: {exception.Message}", exception);
        }
    }

    /// <summary>The frame graph and gauges, frame by frame: the series' first gauge (Frame/UpdateMilliseconds) is each frame's cost, the rest its gauges.</summary>
    private static LiveDiagnosticsSession OpenGaugeSeries(DiagnosticsReportEntry entry, DiagnosticsGaugeSeriesReport report)
    {
        var frameSeries = report.Gauges.FirstOrDefault(static gauge => gauge.Group == "Frame" && gauge.Name == "UpdateMilliseconds");
        var gauges = report.Gauges.Where(gauge => !ReferenceEquals(gauge, frameSeries)).ToList();
        var descriptors = gauges.Select(static gauge => new DiagnosticsLinkGaugeDescriptor(gauge.Group, gauge.Name, Enum.TryParse<GaugeKind>(gauge.Kind, out var kind) ? kind : GaugeKind.Level, GaugeUnits.Of(gauge.Name))).ToList();
        var frameCount = (int)System.Math.Max(report.RecordedFrameCount, frameSeries?.Series.Length ?? 0);
        var history = new DiagnosticsFrameHistory(descriptors.Count, System.Math.Max(1, frameCount));
        var values = new double[descriptors.Count];
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            for (var gaugeIndex = 0; gaugeIndex < gauges.Count; gaugeIndex++)
            {
                var series = gauges[gaugeIndex].Series;
                values[gaugeIndex] = frameIndex < series.Length ? series[frameIndex] : 0;
            }

            var updateMilliseconds = frameSeries is { } frame && frameIndex < frame.Series.Length ? frame.Series[frameIndex] : 0;
            history.Record(report.StartFrame + frameIndex, updateMilliseconds, values);
        }

        return Recorded(entry, report.RandomSeed, nameof(DiagnosticsFeatures.Gauges), report.ProcessId, gauges: descriptors, history: history);
    }

    /// <summary>A periodic report whose frame costs are perFrame, keyed "Category.Group.Item" -- the item is everything after the group, since an item's name may hold dots.</summary>
    private static DiagnosticsReport PerFrameReport(DateTime timestampUtc, int? seed, IReadOnlyDictionary<string, double> perFrame)
    {
        var update = new Dictionary<string, List<DiagnosticsReportFrameCost>>();
        var draw = new Dictionary<string, List<DiagnosticsReportFrameCost>>();
        foreach (var (key, milliseconds) in perFrame)
        {
            var parts = key.Split('.', 3);
            if (parts.Length < 3 || !Enum.TryParse<FrameCostCategory>(parts[0], ignoreCase: true, out var category) || !Enum.IsDefined(category))
            {
                continue;
            }

            var groups = category == FrameCostCategory.Draw ? draw : update;
            if (!groups.TryGetValue(parts[1], out var items))
            {
                items = [];
                groups[parts[1]] = items;
            }

            items.Add(new DiagnosticsReportFrameCost(parts[2], milliseconds));
        }

        return new DiagnosticsReport(
            timestampUtc,
            nameof(DiagnosticsFeatures.FrameBudget),
            seed,
            new DiagnosticsReportFrameBudget(ReadOnly(update), ReadOnly(draw)),
            null,
            null,
            null);
    }

    private static Dictionary<string, IReadOnlyList<DiagnosticsReportFrameCost>> ReadOnly(Dictionary<string, List<DiagnosticsReportFrameCost>> groups) =>
        groups.ToDictionary(static group => group.Key, static group => (IReadOnlyList<DiagnosticsReportFrameCost>)group.Value);

    private static LiveDiagnosticsSession Recorded(
        DiagnosticsReportEntry entry,
        int? seed,
        string features,
        int processId = 0,
        DiagnosticsReport? periodicReport = null,
        DiagnosticsStartupReport? startupReport = null,
        IReadOnlyList<DiagnosticsLinkGaugeDescriptor>? gauges = null,
        DiagnosticsFrameHistory? history = null,
        bool frameCostsArePerFrame = false) =>
        LiveDiagnosticsSession.Recorded(
            entry.FilePath,
            new DiagnosticsLinkHello(DiagnosticsLinkProtocol.Version, processId, seed ?? entry.Seed, features),
            gauges ?? [],
            history,
            periodicReport,
            startupReport,
            frameCostsArePerFrame);
}
