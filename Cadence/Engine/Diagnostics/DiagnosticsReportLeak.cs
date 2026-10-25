namespace Engine.Diagnostics;

/// <summary>One leak detector finding in a report -- an indicator, not proof.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsReportLeak(string Subject, string Detail, double GrowthRatio);
