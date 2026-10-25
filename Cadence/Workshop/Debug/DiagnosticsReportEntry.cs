namespace Workshop.Debug;

/// <summary>One report file in the Reports browser: its kind and path, what it was run with -- seed and frame range where it has them -- what it holds, and a short detail line.</summary>
/// <param name="Detail">What sets it apart (a run's configuration and commit, a report's size), or why it couldn't be read.</param>
/// <param name="Contents">The views it has something for: frame costs, memory, leaks, startup, gauges.</param>
public sealed record DiagnosticsReportEntry(DiagnosticsReportKind Kind, string FilePath, DateTime TimestampUtc, int? Seed, long? StartFrame, long? EndFrame, string Detail, IReadOnlyCollection<LiveView> Contents)
{
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Anything but an A/B, which is already a comparison: something Compare takes.</summary>
    public bool CanBeCompared => Kind != DiagnosticsReportKind.AbComparison;

    public bool Holds(LiveView view) => Contents.Contains(view);
}
