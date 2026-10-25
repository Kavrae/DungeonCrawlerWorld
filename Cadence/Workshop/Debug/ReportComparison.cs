using Engine.Diagnostics;

namespace Workshop.Debug;

/// <summary>Two reports compared, one tab per kind of content: frame costs, memory, leaks and startup.</summary>
/// <param name="PreviousName">The baseline's file.</param>
/// <param name="CurrentName">The file compared with it.</param>
public sealed record ReportComparison(string PreviousName, string CurrentName, IReadOnlyList<ReportComparisonTab> Tabs)
{
    /// <summary>What can be compared, in the order the tabs show: gauges are series over time, not values two reports share, so they aren't one.</summary>
    public static readonly IReadOnlyList<LiveView> Views = [LiveView.FrameCosts, LiveView.Memory, LiveView.Leaks, LiveView.Startup];

    public ReportComparisonTab TabFor(LiveView view) => Tabs.First(tab => tab.View == view);

    /// <summary>Compares every kind of content previous and current both hold, through BenchmarkComparison's rules with thresholdPercent; a kind one of them lacks says which.</summary>
    public static ReportComparison Compare(string previousName, LiveDiagnosticsSession previous, string currentName, LiveDiagnosticsSession current, double thresholdPercent) =>
        new(previousName, currentName,
        [
            CompareFrameCosts(previousName, previous, currentName, current, thresholdPercent),
            CompareMaps(LiveView.Memory, "MB", 0.01, "memory", previousName, MemoryOf(previous), currentName, MemoryOf(current), thresholdPercent),
            CompareMaps(LiveView.Leaks, "% growth", 5, "leak findings", previousName, LeaksOf(previous), currentName, LeaksOf(current), thresholdPercent),
            CompareMaps(LiveView.Startup, "ms", 1, "startup report", previousName, StartupOf(previous), currentName, StartupOf(current), thresholdPercent),
        ]);

    /// <summary>A report's frame costs by "Category.Group.Item" -- the names the phase-performance-testing skill keys its runs by.</summary>
    public static IReadOnlyDictionary<string, double> FrameCostsOf(DiagnosticsReport report) =>
        DiagnosticsReportReader.FrameCostsOf(report).ToDictionary(static entry => $"{entry.Category}.{entry.GroupName}.{entry.ItemName}", static entry => entry.MillisecondsPerSecond);

    /// <summary>A startup report's phases' milliseconds, each keyed by its path through the scope tree ("World Session Setup / Configure / Health"), so a phase that runs in several places stays apart.</summary>
    public static IReadOnlyDictionary<string, double> StartupPhasesOf(DiagnosticsStartupReport report)
    {
        var phases = new Dictionary<string, double>();
        var path = new List<string>();
        foreach (var phase in report.Phases)
        {
            path.RemoveRange(System.Math.Min(phase.Depth, path.Count), path.Count - System.Math.Min(phase.Depth, path.Count));
            path.Add(phase.Name);
            var key = string.Join(" / ", path);
            phases[key] = phases.GetValueOrDefault(key) + phase.Milliseconds;
        }

        return phases;
    }

    private static ReportComparisonTab CompareFrameCosts(string previousName, LiveDiagnosticsSession previous, string currentName, LiveDiagnosticsSession current, double thresholdPercent)
    {
        var previousCosts = previous.LatestPeriodicReport is { FrameBudget: not null } previousReport ? FrameCostsOf(previousReport) : null;
        var currentCosts = current.LatestPeriodicReport is { FrameBudget: not null } currentReport ? FrameCostsOf(currentReport) : null;
        if (previousCosts is not null && currentCosts is not null && previous.FrameCostsArePerFrame != current.FrameCostsArePerFrame)
        {
            return new ReportComparisonTab(LiveView.FrameCosts, [], "ms", "Not comparable: one holds milliseconds per frame (a benchmark run), the other per second (a periodic report).");
        }

        var arePerFrame = previous.FrameCostsArePerFrame || current.FrameCostsArePerFrame;
        return CompareMaps(LiveView.FrameCosts, arePerFrame ? "ms/frame" : "ms/s", arePerFrame ? BenchmarkComparison.DefaultMinimumMillisecondsPerFrame : 1.2, "frame costs", previousName, previousCosts, currentName, currentCosts, thresholdPercent);
    }

    private static ReportComparisonTab CompareMaps(LiveView view, string unit, double minimumChange, string content, string previousName, IReadOnlyDictionary<string, double>? previous, string currentName, IReadOnlyDictionary<string, double>? current, double thresholdPercent)
    {
        var missing = (previous, current) switch
        {
            (null, null) => $"Neither file holds {content}.",
            (null, _) => $"{previousName} holds no {content}.",
            (_, null) => $"{currentName} holds no {content}.",
            _ => null,
        };

        return missing is not null || previous is null || current is null
            ? new ReportComparisonTab(view, [], unit, missing)
            : new ReportComparisonTab(view, BenchmarkComparison.Compare(current, previous, thresholdPercent, minimumChange), unit, null);
    }

    private static IReadOnlyDictionary<string, double>? MemoryOf(LiveDiagnosticsSession session) =>
        session.LatestPeriodicReport?.Memory?.GroupBy(static item => item.ComponentType).ToDictionary(static group => group.Key, static group => group.Sum(static item => item.EstimatedBytes) / (1024.0 * 1024.0));

    private static IReadOnlyDictionary<string, double>? LeaksOf(LiveDiagnosticsSession session) =>
        session.LatestPeriodicReport?.Leaks?.GroupBy(static leak => leak.Subject).ToDictionary(static group => group.Key, static group => group.Max(static leak => leak.GrowthRatio) * 100);

    private static IReadOnlyDictionary<string, double>? StartupOf(LiveDiagnosticsSession session) =>
        session.StartupReport is { } startup ? StartupPhasesOf(startup) : null;
}
