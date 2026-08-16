namespace Engine.Diagnostics;

/// <summary>One leak-symptom flag from LeakDetector's trend evaluation.</summary>
/// <remarks>A heuristic indicator, not proof -- see LeakDetector's own doc comment.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct LeakFinding(string Subject, string Detail, double GrowthRatio);
