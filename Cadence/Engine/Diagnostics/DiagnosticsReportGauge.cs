using System.Text.Json.Serialization;
using static Engine.Diagnostics.DiagnosticsReportRounding;

namespace Engine.Diagnostics;

/// <summary>One gauge in a report, summarized since the previous one: a level's last, minimum and maximum, or a counter's total, largest frame and frames it moved on.</summary>
/// <param name="Kind">GaugeKind as its name.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsReportGauge(
    string Group,
    string Name,
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Last,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Minimum,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Maximum,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Total,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? MaximumPerFrame,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? FramesNonzero)
{
    public static DiagnosticsReportGauge From(GaugeIntervalSummary summary) => summary.Kind is GaugeKind.Cumulative
        ? new DiagnosticsReportGauge(summary.GroupName, summary.GaugeName, summary.Kind.ToString(), null, null, null, RoundForReport(summary.Total), RoundForReport(summary.Maximum), summary.FramesNonzero)
        : new DiagnosticsReportGauge(summary.GroupName, summary.GaugeName, summary.Kind.ToString(), RoundForReport(summary.Last), RoundForReport(summary.Minimum), RoundForReport(summary.Maximum), null, null, null);
}
