using System.Text.Json.Serialization;

namespace Engine.Diagnostics;

/// <summary>One component pool in a report: how many hold it, its estimated bytes, and the population that can hold it.</summary>
/// <param name="Population">"Every entity", or the game's partial population's name (e.g. "Built"); null when the game gave no population split.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsReportMemoryItem(
    string ComponentType,
    int Count,
    long EstimatedBytes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Population = null);
