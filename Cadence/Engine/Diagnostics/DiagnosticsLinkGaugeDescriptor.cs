namespace Engine.Diagnostics;

/// <summary>One gauge a session's frame records carry a value for.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsLinkGaugeDescriptor(string Group, string Name, GaugeKind Kind, GaugeUnit Unit);
