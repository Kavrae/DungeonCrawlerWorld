namespace Engine.Diagnostics;

/// <summary>One completed startup phase's name and wall-clock duration.</summary>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct PhaseRecord(string Name, double Milliseconds);
