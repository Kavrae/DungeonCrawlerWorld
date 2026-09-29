namespace Engine.Diagnostics;

/// <summary>One startup phase's name, wall-clock duration and nesting depth.</summary>
/// <param name="Depth">0 for a top-level phase, 1 for a phase inside it, and so on.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct PhaseRecord(string Name, double Milliseconds, int Depth);
