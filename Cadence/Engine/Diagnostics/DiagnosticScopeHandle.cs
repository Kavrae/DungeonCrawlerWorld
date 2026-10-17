using System.Diagnostics;

namespace Engine.Diagnostics;

/// <summary>An open scope from EngineHooks.DiagnosticScope; disposing it ends the scope.</summary>
/// <remarks>A struct, so `using (EngineHooks.DiagnosticScope(...))` allocates nothing; with no listener it holds nothing and Dispose does nothing.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public readonly struct DiagnosticScopeHandle : IDisposable
{
    private readonly IDiagnosticScopeListener? _listener;
    private readonly DiagnosticScope _scope;
    private readonly long _startTimestamp;

    internal DiagnosticScopeHandle(IDiagnosticScopeListener listener, DiagnosticScope scope)
    {
        _listener = listener;
        _scope = scope;
        listener.ScopeStarted(in scope);
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    public void Dispose() => _listener?.ScopeEnded(in _scope, Stopwatch.GetElapsedTime(_startTimestamp));
}
