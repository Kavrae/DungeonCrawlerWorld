namespace Engine.Diagnostics;

/// <summary>One item in a benchmark report: its total over the range, per frame, and its largest single recording.</summary>
/// <param name="WorstMilliseconds">The largest single recording -- one frame's cost for a system or EcsContext.Update, one dispatch for an event.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsBenchmarkItem(string Name, double TotalMilliseconds, double MillisecondsPerFrame, double WorstMilliseconds);
