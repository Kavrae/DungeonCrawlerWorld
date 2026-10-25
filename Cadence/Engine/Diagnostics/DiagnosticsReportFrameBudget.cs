namespace Engine.Diagnostics;

/// <summary>A report's frame costs: per category, each group's items, costliest first.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsReportFrameBudget(
    IReadOnlyDictionary<string, IReadOnlyList<DiagnosticsReportFrameCost>> Update,
    IReadOnlyDictionary<string, IReadOnlyList<DiagnosticsReportFrameCost>> Draw);
