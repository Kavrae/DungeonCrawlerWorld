namespace Engine.Diagnostics;

/// <summary>One point-in-time sample of GC/entity/component state, used by LeakDetector to detect leak-symptom trends.</summary>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct LeakSample(
    DateTime TimestampUtc,
    long TotalManagedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int LiveEntityCount,
    IReadOnlyDictionary<string, int> ComponentCounts);
