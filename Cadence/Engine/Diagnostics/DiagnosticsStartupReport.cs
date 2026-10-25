namespace Engine.Diagnostics;

/// <summary>The startup report, as its file holds it and the diagnostics link sends it: whether frame pacing settled and how long it took, and every startup phase in start order with its nesting depth.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsStartupReport(DateTime TimestampUtc, bool IsStable, double? TimeToStableMilliseconds, IReadOnlyList<PhaseRecord> Phases);
