using System.Text.Json.Serialization;

namespace Engine.Diagnostics;

/// <summary>One item's cost in a report: milliseconds spent in it over the last full second, and what it ran inside.</summary>
/// <param name="Parents">Every measurement the item ran directly inside; null for a top-level item.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsReportFrameCost(
    string Name,
    double MillisecondsPerSecond,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<DiagnosticsReportFrameCostParent>? Parents = null);
