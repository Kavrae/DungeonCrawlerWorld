namespace Engine.Diagnostics;

/// <summary>Rolling once-per-second wall-clock cost tracker, keyed by (category, group, item).</summary>
/// <remarks>
/// Record(category, groupName, itemName, elapsed) once per occurrence, pairing it with
/// Stopwatch.GetTimestamp()/GetElapsedTime() at the call site so the measurement itself
/// allocates nothing.
///
/// Snapshot exposes the last full second's per-entry totals, sorted descending by cost --
/// DiagnosticsReportWriter regroups this flat list into "Update vs Draw, then per group" for
/// file output. TopEntries exposes the same ranking collapsed to a single "GroupName.ItemName"
/// label per entry, for callers (e.g. DebugWindowContent) that just want the single largest
/// overall contributor.
///
/// Intended as an opt-in diagnostic (see SystemManager.Profiler, EventBus.Profiler,
/// GameShellContext, and DiagnosticsEngine) for tracking frame budgets. Supersedes the old
/// PhaseProfiler, whose flat string-keyed phases couldn't distinguish Update from Draw or say
/// which system/window a phase belonged to.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class FrameBudgetTracker : IFrameCostRecorder
{
    private static readonly long TicksBetweenSamples = TimeSpan.TicksPerSecond;

    private readonly Dictionary<(FrameCostCategory Category, string GroupName, string ItemName), double> _millisecondsSinceLastSample = [];
    private readonly List<FrameCostEntry> _snapshot = [];
    private readonly List<(string Name, double MillisecondsPerSecond)> _topEntries = [];

    private long _lastSampleTicks = DateTime.UtcNow.Ticks;

    /// <summary>Every entry recorded over the last full second, descending by wall-clock cost.</summary>
    /// <remarks>Empty until the first full second elapses.</remarks>
    public IReadOnlyList<FrameCostEntry> Snapshot => _snapshot;

    /// <summary>Same ranking as Snapshot, collapsed to a single "GroupName.ItemName" label per entry.</summary>
    /// <remarks>[0] is always the single largest contributor. Empty until the first full second elapses.</remarks>
    public IReadOnlyList<(string Name, double MillisecondsPerSecond)> TopEntries => _topEntries;

    /// <summary>Records the elapsed time for one item.</summary>
    public void Record(FrameCostCategory category, string groupName, string itemName, TimeSpan elapsed)
    {
        var key = (category, groupName, itemName);
        _millisecondsSinceLastSample[key] = _millisecondsSinceLastSample.GetValueOrDefault(key) + elapsed.TotalMilliseconds;

        var currentTicks = DateTime.UtcNow.Ticks;
        if (currentTicks - _lastSampleTicks < TicksBetweenSamples)
        {
            return;
        }

        _snapshot.Clear();
        _topEntries.Clear();
        foreach (var (entryKey, milliseconds) in _millisecondsSinceLastSample)
        {
            _snapshot.Add(new FrameCostEntry(entryKey.Category, entryKey.GroupName, entryKey.ItemName, milliseconds));
            _topEntries.Add(($"{entryKey.GroupName}.{entryKey.ItemName}", milliseconds));
        }

        _snapshot.Sort(static (a, b) => b.MillisecondsPerSecond.CompareTo(a.MillisecondsPerSecond));
        _topEntries.Sort(static (a, b) => b.MillisecondsPerSecond.CompareTo(a.MillisecondsPerSecond));

        _millisecondsSinceLastSample.Clear();
        _lastSampleTicks = currentTicks;
    }
}
