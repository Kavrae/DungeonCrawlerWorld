namespace Engine.Diagnostics;

/// <summary>A periodic diagnostics report, as latest.json holds it and the diagnostics link sends it: written by DiagnosticsReportWriter, read by DiagnosticsReportReader.</summary>
/// <remarks>Each section is null when the feature behind it was off for the run.</remarks>
/// <param name="Features">DiagnosticsFeatures as its name.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsReport(
    DateTime TimestampUtc,
    string Features,
    int? RandomSeed,
    DiagnosticsReportFrameBudget? FrameBudget,
    IReadOnlyList<DiagnosticsReportMemoryItem>? Memory,
    IReadOnlyList<DiagnosticsReportLeak>? Leaks,
    IReadOnlyList<DiagnosticsReportGauge>? Gauges);
