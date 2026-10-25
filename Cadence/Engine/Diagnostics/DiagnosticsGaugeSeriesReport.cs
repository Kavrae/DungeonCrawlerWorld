namespace Engine.Diagnostics;

/// <summary>A benchmark range's gauges, as gauges-*.json holds them: every gauge's value on every frame of the range, the frame cost first -- written by GaugeSeriesReport, read by DiagnosticsReportReader.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsGaugeSeriesReport(
    DateTime TimestampUtc,
    int? RandomSeed,
    int ProcessId,
    long StartFrame,
    long EndFrame,
    long FrameCount,
    long RecordedFrameCount,
    IReadOnlyList<DiagnosticsGaugeSeriesItem> Gauges);
