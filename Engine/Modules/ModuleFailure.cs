namespace Engine.Modules;

/// <summary>A module that failed to load or register, and why -- reported, never thrown, so one broken mod never blocks the rest.</summary>
public readonly record struct ModuleFailure(string Source, Exception Exception);
