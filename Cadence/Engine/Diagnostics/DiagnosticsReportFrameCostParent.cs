namespace Engine.Diagnostics;

/// <summary>A measurement a report's frame cost ran inside, by name.</summary>
/// <param name="Category">FrameCostCategory as its name.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsReportFrameCostParent(string Category, string Group, string Item);
