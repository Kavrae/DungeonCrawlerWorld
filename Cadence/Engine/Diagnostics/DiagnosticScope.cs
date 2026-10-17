namespace Engine.Diagnostics;

/// <summary>A named span of work emitted through EngineHooks.DiagnosticScopes, such as a startup step or one module's build phase.</summary>
/// <param name="Name">A constant name, e.g. "Entity Population" or "RegisterComponents".</param>
/// <param name="Detail">What the span ran for, e.g. a module's name; null when the name says it all.</param>
/// <remarks>Both parts are strings an emit site already holds, so emitting builds no string; a listener that wants one joins them with <see cref="ToString"/>.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct DiagnosticScope(string Name, string? Detail = null)
{
    /// <summary>"Name:Detail", or Name alone.</summary>
    public override string ToString() => Detail is null ? Name : $"{Name}:{Detail}";
}
