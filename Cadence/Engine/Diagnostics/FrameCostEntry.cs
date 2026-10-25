namespace Engine.Diagnostics;

/// <summary>One item's total wall-clock cost over the last full sampled second, and what it was measured inside.</summary>
/// <remarks>GroupName is the recorder (e.g. "SystemManager", "EventBus", a window tier name); ItemName is the specific system/event/window type within it.</remarks>
/// <param name="Parents">Every measurement this item ran directly inside, by name -- an event published from several systems has each; null or empty for a top-level item.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct FrameCostEntry(FrameCostCategory Category, string GroupName, string ItemName, double MillisecondsPerSecond, IReadOnlyList<FrameCostRowName>? Parents = null);
