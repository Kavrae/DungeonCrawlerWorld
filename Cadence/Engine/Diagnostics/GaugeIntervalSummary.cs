namespace Engine.Diagnostics;

/// <summary>One gauge's readings since the previous periodic report.</summary>
/// <param name="Last">The last frame's value: the level as read, or a cumulative gauge's change that frame.</param>
/// <param name="Minimum">The smallest per-frame value in the interval.</param>
/// <param name="Maximum">The largest per-frame value in the interval.</param>
/// <param name="Total">The sum of every per-frame value in the interval -- meaningful for a cumulative gauge only.</param>
/// <param name="FramesNonzero">How many frames in the interval had a nonzero value -- meaningful for a cumulative gauge only.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct GaugeIntervalSummary(
    string GroupName,
    string GaugeName,
    GaugeKind Kind,
    double Last,
    double Minimum,
    double Maximum,
    double Total,
    int FramesNonzero);
