namespace Engine.Diagnostics;

/// <summary>One frame-cost row's name: its category, its group (the recorder) and its item (the system, event or window).</summary>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct FrameCostRowName(FrameCostCategory Category, string GroupName, string ItemName);
