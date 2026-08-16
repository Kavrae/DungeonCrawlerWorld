namespace Engine.Diagnostics;

/// <summary>Which diagnostics engine features are active for this run.</summary>
/// <remarks>Defaults to None -- diagnostics are opt-in, off unless a caller explicitly requests them. See DiagnosticsEngine, DiagnosticsFeaturesParser, GameLoop.</remarks>
/// <cleanupVersion>1</cleanupVersion>
[Flags]
public enum DiagnosticsFeatures
{
    None = 0,
    FrameBudget = 1 << 0,
    Memory = 1 << 1,
    Startup = 1 << 2,
    LeakDetection = 1 << 3,
    All = FrameBudget | Memory | Startup | LeakDetection,
}
