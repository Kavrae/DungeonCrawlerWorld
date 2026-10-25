namespace Engine.Diagnostics;

/// <summary>The files a benchmark range wrote when it closed.</summary>
/// <param name="MemoryReportPath">The pool memory report, when Memory was on.</param>
/// <param name="GaugeReportPath">The gauge series report, when Gauges was on.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsLinkBenchmarkCompletion(long StartFrame, long EndFrame, string BenchmarkReportPath, string? MemoryReportPath, string? GaugeReportPath);
