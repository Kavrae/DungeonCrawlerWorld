namespace Engine.Diagnostics;

/// <summary>One component type's last-sampled instance count and estimated memory footprint.</summary>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct ComponentMemoryEntry(string ComponentTypeName, int Count, long EstimatedBytes);
