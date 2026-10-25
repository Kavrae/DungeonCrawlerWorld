namespace Engine.Diagnostics;

/// <summary>One component type's last-sampled instance count and estimated memory footprint, and the population that can hold it.</summary>
/// <param name="Population">"Every entity", or the game's partial population's name; null when the game gave no population split.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct ComponentMemoryEntry(string ComponentTypeName, int Count, long EstimatedBytes, string? Population = null);
