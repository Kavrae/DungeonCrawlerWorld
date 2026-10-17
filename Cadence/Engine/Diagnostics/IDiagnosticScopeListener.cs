namespace Engine.Diagnostics;

/// <summary>Listens to the EngineHooks.DiagnosticScopes channel.</summary>
/// <remarks>Scopes nest: every ScopeStarted is matched by one ScopeEnded, innermost first.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IDiagnosticScopeListener
{
    void ScopeStarted(in DiagnosticScope scope);

    void ScopeEnded(in DiagnosticScope scope, TimeSpan elapsed);
}
