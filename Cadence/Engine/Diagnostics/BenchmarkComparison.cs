namespace Engine.Diagnostics;

/// <summary>Compares two benchmark runs' milliseconds per frame, item by item, the way the phase-performance-testing skill compares a run with a saved one.</summary>
/// <remarks>
/// Every item either run has gets a row; an item one run lacks reads 0 there. The change is current less previous, and its
/// percentage is of previous, rounded to one decimal place, or none when previous is 0. A row is a regression when it rose
/// by at least minimumMillisecondsPerFrame and at least thresholdPercent, and an improvement when it fell by both; an item
/// only the current run has is a regression once it reaches minimumMillisecondsPerFrame, since any percentage of nothing
/// passes the threshold. Rows are ordered by the size of their change, largest first.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class BenchmarkComparison
{
    /// <summary>The skill's threshold for a single run against a saved one.</summary>
    public const double DefaultThresholdPercent = 20;

    /// <summary>The skill's smallest change worth flagging.</summary>
    public const double DefaultMinimumMillisecondsPerFrame = 0.02;

    public static IReadOnlyList<BenchmarkComparisonRow> Compare(
        IReadOnlyDictionary<string, double> current,
        IReadOnlyDictionary<string, double> previous,
        double thresholdPercent = DefaultThresholdPercent,
        double minimumMillisecondsPerFrame = DefaultMinimumMillisecondsPerFrame)
    {
        var rows = new List<BenchmarkComparisonRow>();
        foreach (var name in current.Keys.Concat(previous.Keys).Distinct())
        {
            var currentValue = current.GetValueOrDefault(name);
            var previousValue = previous.GetValueOrDefault(name);
            var change = currentValue - previousValue;
            double? changePercent = previousValue > 0 ? System.Math.Round(change / previousValue * 100, 1) : null;
            double? ratio = previousValue > 0 ? currentValue / previousValue : null;
            var flag = changePercent is not { } percent
                ? (previousValue == 0 && change >= minimumMillisecondsPerFrame ? BenchmarkComparisonFlag.Regression : BenchmarkComparisonFlag.None)
                : change >= minimumMillisecondsPerFrame && percent >= thresholdPercent ? BenchmarkComparisonFlag.Regression
                : -change >= minimumMillisecondsPerFrame && -percent >= thresholdPercent ? BenchmarkComparisonFlag.Improvement
                : BenchmarkComparisonFlag.None;
            rows.Add(new BenchmarkComparisonRow(name, currentValue, previousValue, change, changePercent, ratio, flag));
        }

        rows.Sort(static (first, second) => System.Math.Abs(second.Change).CompareTo(System.Math.Abs(first.Change)));
        return rows;
    }
}
