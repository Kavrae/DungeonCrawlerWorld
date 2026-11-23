using Engine.Diagnostics;
using Workshop.Debug;

namespace Workshop.Tests;

/// <summary>The Reports browser against real reports of every kind, checked in as fixtures: each opens, and Compare's numbers agree with the phase-performance-testing skill's own.</summary>
[TestClass]
public sealed class DiagnosticsReportFolderTests
{
    private static IReadOnlyList<string> ReportDirectories() => [Path.Combine(AppContext.BaseDirectory, "Fixtures", "Reports")];

    [TestMethod]
    public void TheFixtures_HoldEveryKindOfReport()
    {
        var kinds = DiagnosticsReportCatalog.Scan(ReportDirectories()).Select(static entry => entry.Kind).ToHashSet();

        CollectionAssert.AreEquivalent(Enum.GetValues<DiagnosticsReportKind>(), kinds.ToArray());
    }

    [TestMethod]
    public void EveryReportInTheFolders_ReadsAndOpens()
    {
        var entries = DiagnosticsReportCatalog.Scan(ReportDirectories());
        var failures = new List<string>();
        foreach (var entry in entries)
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

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    /// <summary>Each A/B the skill saved holds its own change per system; the comparison of its two sides gives the same.</summary>
    /// <remarks>The A/B stores each side rounded to four decimals but took its percent from the unrounded medians, so the two agree to within the percent's own rounding plus what four-decimal rounding of each side can move it.</remarks>
    [TestMethod]
    public void EveryABTheSkillSaved_ComparesToTheChangesItRecorded()
    {
        var abEntries = DiagnosticsReportCatalog.Scan(ReportDirectories()).Where(static entry => entry.Kind == DiagnosticsReportKind.AbComparison).ToList();
        Assert.IsNotEmpty(abEntries);

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
