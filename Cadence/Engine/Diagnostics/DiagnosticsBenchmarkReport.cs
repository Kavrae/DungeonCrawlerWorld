namespace Engine.Diagnostics;

/// <summary>A benchmark range's report, as benchmark-*.json holds it: every frame cost recorded inside the range, totalled and per frame -- written by FrameRangeBenchmark, read by DiagnosticsReportReader.</summary>
/// <param name="Conditions">What the run measured under; null in a report written before it was kept.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsBenchmarkReport(
    DateTime TimestampUtc,
    int? RandomSeed,
    int ProcessId,
    long StartFrame,
    long EndFrame,
    long FrameCount,
    double WallClockMilliseconds,
    IReadOnlyDictionary<string, IReadOnlyList<DiagnosticsBenchmarkItem>> Update,
    IReadOnlyDictionary<string, IReadOnlyList<DiagnosticsBenchmarkItem>> Draw,
    DiagnosticsRunConditions? Conditions = null);
