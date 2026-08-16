using System.Text.Json;

namespace Engine.Diagnostics;

/// <summary>Serializes DiagnosticsEngine's current snapshots to a dedicated diagnostics folder.</summary>
/// <remarks>
/// Writes latest.json (machine-readable, re-written on every call so a caller polling the file
/// always sees the newest snapshot) and latest.txt (a short plain-text summary alongside it, for
/// a human or Claude to skim without parsing JSON). Both are cheap enough to write on the same
/// cadence DiagnosticsEngine's caller already uses for its console dump.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
internal static class DiagnosticsReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Write(
        string outputDirectory,
        DiagnosticsFeatures features,
        IReadOnlyList<FrameCostEntry>? frameBudgetSnapshot,
        IReadOnlyList<ComponentMemoryEntry>? componentMemorySnapshot,
        IReadOnlyList<LeakFinding>? leakFindings)
    {
        Directory.CreateDirectory(outputDirectory);

        var report = new DiagnosticsReport(
            DateTime.UtcNow,
            features.ToString(),
            frameBudgetSnapshot is null ? null : BuildFrameBudgetSection(frameBudgetSnapshot),
            componentMemorySnapshot?.Select(static entry => new ComponentMemoryItem(entry.ComponentTypeName, entry.Count, entry.EstimatedBytes)).ToList(),
            leakFindings?.Select(static finding => new LeakFindingItem(finding.Subject, finding.Detail, finding.GrowthRatio)).ToList());

        File.WriteAllText(Path.Combine(outputDirectory, "latest.json"), JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(Path.Combine(outputDirectory, "latest.txt"), BuildTextSummary(report));
    }

    private static FrameBudgetSection BuildFrameBudgetSection(IReadOnlyList<FrameCostEntry> snapshot)
    {
        return new FrameBudgetSection(
            GroupByCategory(snapshot, FrameCostCategory.Update),
            GroupByCategory(snapshot, FrameCostCategory.Draw));
    }

    private static Dictionary<string, List<FrameCostItem>> GroupByCategory(IReadOnlyList<FrameCostEntry> snapshot, FrameCostCategory category)
    {
        var groups = new Dictionary<string, List<FrameCostItem>>();
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

            items.Add(new FrameCostItem(entry.ItemName, entry.MillisecondsPerSecond));
        }

        foreach (var items in groups.Values)
        {
            items.Sort(static (a, b) => b.MillisecondsPerSecond.CompareTo(a.MillisecondsPerSecond));
        }

        return groups;
    }

    private static string BuildTextSummary(DiagnosticsReport report)
    {
        var lines = new List<string> { $"[Diagnostics] {report.TimestampUtc:O} -- features: {report.Features}" };

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

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendCategory(List<string> lines, string categoryName, Dictionary<string, List<FrameCostItem>> groups)
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

    private sealed record DiagnosticsReport(DateTime TimestampUtc, string Features, FrameBudgetSection? FrameBudget, List<ComponentMemoryItem>? Memory, List<LeakFindingItem>? Leaks);

    private sealed record FrameBudgetSection(Dictionary<string, List<FrameCostItem>> Update, Dictionary<string, List<FrameCostItem>> Draw);

    private sealed record FrameCostItem(string Name, double MillisecondsPerSecond);

    private sealed record ComponentMemoryItem(string ComponentType, int Count, long EstimatedBytes);

    private sealed record LeakFindingItem(string Subject, string Detail, double GrowthRatio);
}
