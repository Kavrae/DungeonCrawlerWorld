using System.Text.Json;
using static Engine.Diagnostics.DiagnosticsReportRounding;

namespace Engine.Diagnostics;

/// <summary>Serializes DiagnosticsEngine's current snapshots to a dedicated diagnostics folder, and returns the JSON it wrote.</summary>
/// <remarks>
/// Writes latest.json (machine-readable, a DiagnosticsReport, re-written on every call so a caller polling the file
/// always sees the newest snapshot) and latest.txt (a short plain-text summary alongside it, for
/// a human or Claude to skim without parsing JSON). Both are cheap enough to write on the same
/// cadence DiagnosticsEngine's caller already uses for its console dump. DiagnosticsReportReader reads the JSON back.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class DiagnosticsReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <returns>The JSON written to latest.json.</returns>
    public static string Write(
        string outputDirectory,
        DiagnosticsFeatures features,
        int? randomSeed,
        IReadOnlyList<FrameCostEntry>? frameBudgetSnapshot,
        IReadOnlyList<ComponentMemoryEntry>? componentMemorySnapshot,
        IReadOnlyList<LeakFinding>? leakFindings,
        IReadOnlyList<GaugeIntervalSummary>? gaugeSummaries)
    {
        Directory.CreateDirectory(outputDirectory);

        var report = Build(DateTime.UtcNow, features, randomSeed, frameBudgetSnapshot, componentMemorySnapshot, leakFindings, gaugeSummaries);
        var reportJson = Serialize(report);
        File.WriteAllText(Path.Combine(outputDirectory, "latest.json"), reportJson);
        File.WriteAllText(Path.Combine(outputDirectory, "latest.txt"), BuildTextSummary(report));
        return reportJson;
    }

    public static DiagnosticsReport Build(
        DateTime timestampUtc,
        DiagnosticsFeatures features,
        int? randomSeed,
        IReadOnlyList<FrameCostEntry>? frameBudgetSnapshot,
        IReadOnlyList<ComponentMemoryEntry>? componentMemorySnapshot,
        IReadOnlyList<LeakFinding>? leakFindings,
        IReadOnlyList<GaugeIntervalSummary>? gaugeSummaries) => new(
            timestampUtc,
            features.ToString(),
            randomSeed,
            frameBudgetSnapshot is null ? null : BuildFrameBudgetSection(frameBudgetSnapshot),
            componentMemorySnapshot?.Select(static entry => new DiagnosticsReportMemoryItem(entry.ComponentTypeName, entry.Count, entry.EstimatedBytes, entry.Population)).ToList(),
            leakFindings?.Select(static finding => new DiagnosticsReportLeak(finding.Subject, finding.Detail, finding.GrowthRatio)).ToList(),
            gaugeSummaries?.Select(static summary => DiagnosticsReportGauge.From(summary)).ToList());

    public static string Serialize(DiagnosticsReport report) => JsonSerializer.Serialize(report, JsonOptions);

    private static DiagnosticsReportFrameBudget BuildFrameBudgetSection(IReadOnlyList<FrameCostEntry> snapshot)
    {
        return new DiagnosticsReportFrameBudget(
            GroupByCategory(snapshot, FrameCostCategory.Update),
            GroupByCategory(snapshot, FrameCostCategory.Draw));
    }

    private static Dictionary<string, IReadOnlyList<DiagnosticsReportFrameCost>> GroupByCategory(IReadOnlyList<FrameCostEntry> snapshot, FrameCostCategory category)
    {
        var groups = new Dictionary<string, List<DiagnosticsReportFrameCost>>();
        foreach (var entry in snapshot)
        {
            if (entry.Category != category)
            {
                continue;
            }

            if (!groups.TryGetValue(entry.GroupName, out var items))
            {
                items = [];
                groups[entry.GroupName] = items;
            }

            items.Add(new DiagnosticsReportFrameCost(entry.ItemName, entry.MillisecondsPerSecond, entry.Parents is { Count: > 0 } parents ? [.. parents.Select(static parent => new DiagnosticsReportFrameCostParent(parent.Category.ToString(), parent.GroupName, parent.ItemName))] : null));
        }

        foreach (var items in groups.Values)
        {
            items.Sort(static (a, b) => b.MillisecondsPerSecond.CompareTo(a.MillisecondsPerSecond));
        }

        return groups.ToDictionary(static group => group.Key, static group => (IReadOnlyList<DiagnosticsReportFrameCost>)group.Value);
    }

    private static string BuildTextSummary(DiagnosticsReport report)
    {
        var lines = new List<string> { $"[Diagnostics] {report.TimestampUtc:O} -- features: {report.Features}, seed: {report.RandomSeed?.ToString() ?? "unknown"}" };

        if (report.FrameBudget is { } frameBudget)
        {
            AppendCategory(lines, "Update", frameBudget.Update);
            AppendCategory(lines, "Draw", frameBudget.Draw);
        }

        if (report.Memory is { Count: > 0 } memory)
        {
            lines.Add("Memory (bytes descending):");
            foreach (var item in memory)
            {
                lines.Add($"  {item.ComponentType}: {item.Count:N0} instances, {item.EstimatedBytes:N0} bytes");
            }
        }

        if (report.Leaks is { Count: > 0 } leaks)
        {
            lines.Add("Leak indicators (heuristic, not proof):");
            foreach (var finding in leaks)
            {
                lines.Add($"  {finding.Subject}: {finding.Detail}");
            }
        }

        if (report.Gauges is { Count: > 0 } gauges)
        {
            lines.Add("Gauges (since the previous report):");
            foreach (var gauge in gauges)
            {
                lines.Add(gauge.Kind == nameof(GaugeKind.Cumulative)
                    ? $"  {gauge.Group}/{gauge.Name}: total {FormatForReport(gauge.Total)}, max {FormatForReport(gauge.MaximumPerFrame)}/frame, {gauge.FramesNonzero:N0} frames nonzero"
                    : $"  {gauge.Group}/{gauge.Name}: {FormatForReport(gauge.Last)} (min {FormatForReport(gauge.Minimum)}, max {FormatForReport(gauge.Maximum)})");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendCategory(List<string> lines, string categoryName, IReadOnlyDictionary<string, IReadOnlyList<DiagnosticsReportFrameCost>> groups)
    {
        if (groups.Count == 0)
        {
            return;
        }

        lines.Add($"{categoryName}:");
        foreach (var (groupName, items) in groups)
        {
            lines.Add($"  {groupName}:");
            foreach (var item in items)
            {
                lines.Add($"    {item.Name}: {item.MillisecondsPerSecond:N1}ms/s");
            }
        }
    }
}
