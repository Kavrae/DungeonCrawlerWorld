namespace Engine.Diagnostics;

/// <summary>One point-in-time sample of GC/entity/component state, used by LeakDetector to detect leak-symptom trends.</summary>
/// <param name="PartialPopulationCount">The EntityPopulationPolicy's partial population at the time; null when the session has no policy.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct LeakSample(
    DateTime TimestampUtc,
    long TotalManagedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int LiveEntityCount,
    int? PartialPopulationCount,
    IReadOnlyDictionary<Type, int> ComponentCounts);
