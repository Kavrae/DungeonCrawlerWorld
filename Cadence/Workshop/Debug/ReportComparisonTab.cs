using Engine.Diagnostics;

namespace Workshop.Debug;

/// <summary>One kind of content of two reports compared: its rows, largest change first, in unit -- or, when one of them lacks it, why there are none.</summary>
public sealed record ReportComparisonTab(LiveView View, IReadOnlyList<BenchmarkComparisonRow> Rows, string Unit, string? Unavailable);
