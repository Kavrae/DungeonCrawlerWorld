namespace Engine.Diagnostics;

/// <summary>One item's total wall-clock cost over the last full sampled second.</summary>
/// <remarks>GroupName is the recorder (e.g. "SystemManager", "EventBus", a window tier name); ItemName is the specific system/event/window type within it.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct FrameCostEntry(FrameCostCategory Category, string GroupName, string ItemName, double MillisecondsPerSecond);
