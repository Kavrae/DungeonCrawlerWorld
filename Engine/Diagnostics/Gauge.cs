namespace Engine.Diagnostics;

/// <summary>One named value diagnostics samples once per simulation frame while the Gauges feature is on.</summary>
/// <param name="GroupName">A constant group, the same role as a frame cost's group (e.g. "NeighborhoodStreamer").</param>
/// <param name="GaugeName">A constant name, unique within its group.</param>
/// <param name="Read">Returns the current value; called at the end of a simulation frame, so it reads a counter the owner keeps and never a component pool.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record Gauge(string GroupName, string GaugeName, GaugeKind Kind, Func<double> Read);
