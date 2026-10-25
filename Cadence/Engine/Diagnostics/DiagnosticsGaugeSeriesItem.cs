using System.Text.Json.Serialization;

namespace Engine.Diagnostics;

/// <summary>One gauge's series in a gauge report, with its summary: a level's minimum, maximum and mean, or a counter's total, largest frame and frames it moved on.</summary>
/// <param name="Kind">GaugeKind as its name.</param>
/// <param name="MaximumFrame">The first frame holding Maximum (a level) or MaximumPerFrame (a cumulative gauge).</param>
/// <param name="Series">One value per frame of the range, StartFrame first: a level as read, a cumulative gauge's change that frame.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsGaugeSeriesItem(
    string Group,
    string Name,
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Minimum,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Maximum,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Mean,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Total,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? MaximumPerFrame,
    long MaximumFrame,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? FramesNonzero,
    double[] Series);
