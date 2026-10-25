namespace Engine.Diagnostics;

/// <summary>A simulated session's gauges, in the order each of its frame records lists their values.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsLinkSessionStart(IReadOnlyList<DiagnosticsLinkGaugeDescriptor> Gauges);
