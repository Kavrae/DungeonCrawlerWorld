namespace Engine.Diagnostics;

/// <summary>One item of a benchmark comparison: its milliseconds per frame in each run, the change, the change as a percentage of previous and the ratio (none when previous is 0), and whether it crossed the threshold.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed record BenchmarkComparisonRow(string Name, double Current, double Previous, double Change, double? ChangePercent, double? Ratio, BenchmarkComparisonFlag Flag);
