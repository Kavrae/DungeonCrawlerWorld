using Engine.Diagnostics;
using Workshop.Debug;

namespace Workshop.Tests;

/// <summary>The Reports browser against the reports actually on this machine: every file of every kind in the repository's report folders opens, and Compare's numbers agree with the phase-performance-testing skill's own.</summary>
[TestClass]
public sealed class DiagnosticsReportFolderTests
{
    private const string SolutionFileName = "DungeonCrawlerWorld.sln";

    /// <summary>A gauge series holds every gauge for every frame of its range; the newest few are enough to cover the format.</summary>
    private const int GaugeSeriesOpened = 5;

    private static IReadOnlyList<string> ReportDirectories()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        var log = Path.Combine(directory?.FullName ?? throw new InvalidOperationException("No repository root above the test assembly."), "Log");
        IReadOnlyList<string> directories = [Path.Combine(log, "diagnostics"), Path.Combine(log, "phase-benchmarks")];
        if (!directories.Any(Directory.Exists))
        {
            Assert.Inconclusive("This machine has no report folders.");
        }

        return directories;
    }

    [TestMethod]
    public void EveryReportInTheFolders_ReadsAndOpens()
    {
        var entries = DiagnosticsReportCatalog.Scan(ReportDirectories());
        var failures = new List<string>();
        foreach (var group in entries.GroupBy(static entry => entry.Kind))
        {
            foreach (var entry in group.Key == DiagnosticsReportKind.Gauges ? group.Take(GaugeSeriesOpened) : group)
            {
                try
                {
                    if (entry.Detail.StartsWith("Unreadable", StringComparison.Ordinal))
                    {
                        failures.Add($"{entry.FileName}: {entry.Detail}");
                    }
                    else if (entry.Kind == DiagnosticsReportKind.AbComparison)
                    {
                        DiagnosticsReportFiles.OpenAbComparison(entry);
                    }
                    else
                    {
                        Assert.IsNotNull(DiagnosticsReportFiles.OpenSession(entry));
                    }
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException)
                {
                    failures.Add($"{entry.FileName}: {exception.Message}");
                }
            }
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    /// <summary>Each A/B the skill saved holds its own change per system; the comparison of its two sides gives the same.</summary>
    /// <remarks>The A/B stores each side rounded to four decimals but took its percent from the unrounded medians, so the two agree to within the percent's own rounding plus what four-decimal rounding of each side can move it.</remarks>
    [TestMethod]
    public void EveryABTheSkillSaved_ComparesToTheChangesItRecorded()
    {
        var abEntries = DiagnosticsReportCatalog.Scan(ReportDirectories()).Where(static entry => entry.Kind == DiagnosticsReportKind.AbComparison).ToList();
        if (abEntries.Count == 0)
        {
            Assert.Inconclusive("No A/B on this machine.");
        }

        foreach (var entry in abEntries)
        {
            var (previous, current) = DiagnosticsReportFiles.OpenAbComparison(entry);
            var recorded = RecordedChanges(entry.FilePath);
            foreach (var row in ReportComparison.Compare("A", previous, "B", current, BenchmarkComparison.DefaultThresholdPercent).TabFor(LiveView.FrameCosts).Rows)
            {
                if (recorded.TryGetValue(row.Name, out var recordedPercent) && row.ChangePercent is { } percent)
                {
                    const double StoredRounding = 0.00005;
                    const double PercentRounding = 0.05;
                    if (row.Previous - StoredRounding <= 0)
                    {
                        continue;
                    }

                    var lowest = ((row.Current - StoredRounding) / (row.Previous + StoredRounding) - 1) * 100 - PercentRounding;
                    var highest = ((row.Current + StoredRounding) / (row.Previous - StoredRounding) - 1) * 100 + PercentRounding;
                    Assert.IsTrue(recordedPercent >= lowest && recordedPercent <= highest, $"{entry.FileName}: {row.Name} recorded {recordedPercent}%, compared {percent}% (stored sides allow {lowest:0.0} to {highest:0.0})");
                    Assert.IsTrue(percent >= lowest && percent <= highest, $"{entry.FileName}: {row.Name} compared {percent}% outside {lowest:0.0} to {highest:0.0}");
                }
            }
        }
    }

    private static Dictionary<string, double> RecordedChanges(string filePath)
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(filePath));
        var changes = new Dictionary<string, double>();
        if (!document.RootElement.TryGetProperty("systems", out var systems))
        {
            return changes;
        }

        foreach (var system in systems.EnumerateObject())
        {
            if (system.Value.TryGetProperty("deltaPercent", out var deltaPercent) && deltaPercent.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                changes[system.Name] = deltaPercent.GetDouble();
            }
        }

        return changes;
    }
}
