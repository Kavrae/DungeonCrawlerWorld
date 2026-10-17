namespace Engine.Modules;

/// <summary>A module that failed to load or register, and why.</summary>
/// <remarks>Reported, never thrown, so one broken mod never blocks the rest.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct ModuleFailure(string Source, Exception Exception);