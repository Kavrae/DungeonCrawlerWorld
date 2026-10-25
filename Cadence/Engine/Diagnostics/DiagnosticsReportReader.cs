using System.Text.Json;

namespace Engine.Diagnostics;

/// <summary>Reads the reports diagnostics writes -- the periodic report (latest.json) and the startup report -- whether from a file or off the diagnostics link.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class DiagnosticsReportReader
{
    /// <exception cref="InvalidDataException">json is not a periodic report.</exception>
    public static DiagnosticsReport ReadPeriodicReport(ReadOnlySpan<byte> json) => Read<DiagnosticsReport>(json);

    /// <exception cref="InvalidDataException">json is not a startup report.</exception>
    public static DiagnosticsStartupReport ReadStartupReport(ReadOnlySpan<byte> json) => Read<DiagnosticsStartupReport>(json);

    /// <exception cref="InvalidDataException">json is not a benchmark report.</exception>
    public static DiagnosticsBenchmarkReport ReadBenchmarkReport(ReadOnlySpan<byte> json) => Read<DiagnosticsBenchmarkReport>(json);

    /// <exception cref="InvalidDataException">json is not a gauge series report.</exception>
    public static DiagnosticsGaugeSeriesReport ReadGaugeSeriesReport(ReadOnlySpan<byte> json) => Read<DiagnosticsGaugeSeriesReport>(json);

    /// <exception cref="InvalidDataException">json is not a memory report.</exception>
    public static DiagnosticsMemoryReport ReadMemoryReport(ReadOnlySpan<byte> json) => Read<DiagnosticsMemoryReport>(json);

    /// <summary>A benchmark report's milliseconds per frame by "Category.Group.Item" -- the names the phase-performance-testing skill keys its runs by, so the two compare.</summary>
    public static IReadOnlyDictionary<string, double> MillisecondsPerFrameOf(DiagnosticsBenchmarkReport report)
    {
        var perFrame = new Dictionary<string, double>();
        AddPerFrame(perFrame, FrameCostCategory.Update, report.Update);
        AddPerFrame(perFrame, FrameCostCategory.Draw, report.Draw);
        return perFrame;
    }

    private static void AddPerFrame(Dictionary<string, double> perFrame, FrameCostCategory category, IReadOnlyDictionary<string, IReadOnlyList<DiagnosticsBenchmarkItem>>? groups)
    {
        foreach (var (groupName, items) in groups ?? new Dictionary<string, IReadOnlyList<DiagnosticsBenchmarkItem>>())
        {
            foreach (var item in items)
            {
                perFrame[$"{category}.{groupName}.{item.Name}"] = item.MillisecondsPerFrame;
            }
        }
    }

    /// <summary>A report's frame costs as rows: Update before Draw, each group in the report's order, each group's items costliest first; empty when Frame budget was off.</summary>
    public static IReadOnlyList<FrameCostEntry> FrameCostsOf(DiagnosticsReport report)
    {
        if (report.FrameBudget is not { } frameBudget)
        {
            return [];
        }

        var rows = new List<FrameCostEntry>();
        AddCategory(rows, FrameCostCategory.Update, frameBudget.Update);
        AddCategory(rows, FrameCostCategory.Draw, frameBudget.Draw);
        return rows;
    }

    private static void AddCategory(List<FrameCostEntry> rows, FrameCostCategory category, IReadOnlyDictionary<string, IReadOnlyList<DiagnosticsReportFrameCost>> groups)
    {
        foreach (var (groupName, items) in groups)
        {
            foreach (var item in items)
            {
                rows.Add(new FrameCostEntry(category, groupName, item.Name, item.MillisecondsPerSecond, ParentsOf(item)));
            }
        }
    }

    /// <summary>item's parents by name; one whose category names no FrameCostCategory is left out, so a report from another build still reads.</summary>
    private static FrameCostRowName[]? ParentsOf(DiagnosticsReportFrameCost item)
    {
        if (item.Parents is not { } parents)
        {
            return null;
        }

        var names = new List<FrameCostRowName>(parents.Count);
        foreach (var parent in parents)
        {
            if (Enum.TryParse<FrameCostCategory>(parent.Category, ignoreCase: true, out var parentCategory) && Enum.IsDefined(parentCategory))
            {
                names.Add(new FrameCostRowName(parentCategory, parent.Group, parent.Item));
            }
        }

        return [.. names];
    }

    private static T Read<T>(ReadOnlySpan<byte> json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException($"A {typeof(T).Name} was null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Not a {typeof(T).Name}: {exception.Message}", exception);
        }
    }
}
