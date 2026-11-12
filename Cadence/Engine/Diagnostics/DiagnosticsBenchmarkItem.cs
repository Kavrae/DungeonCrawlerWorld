namespace Engine.Diagnostics;

/// <summary>One item in a benchmark report: its total over the range, per frame, its largest single recording, and how many recordings it took.</summary>
/// <param name="WorstMilliseconds">The largest single recording -- one frame's cost for a system or EcsContext.Update, one dispatch for an event.</param>
/// <param name="RecordCount">How many times it was recorded inside the range, nested measurements of itself included -- what tells "slower" from "called more"; 0 in a report written before it was kept.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsBenchmarkItem(string Name, double TotalMilliseconds, double MillisecondsPerFrame, double WorstMilliseconds, int RecordCount = 0);
