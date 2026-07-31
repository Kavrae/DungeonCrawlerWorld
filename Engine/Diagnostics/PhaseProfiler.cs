namespace Engine.Diagnostics;

/// <summary>
/// Rolling once-per-second wall-clock cost tracker, keyed by phase name -- the time-domain
/// counterpart to PerformanceCounter's per-second rate. Record(name, elapsed) once per
/// occurrence (pair it with Stopwatch.GetTimestamp()/Stopwatch.GetElapsedTime at the call site
/// so the measurement itself allocates nothing); TopPhases exposes the last full second's
/// per-phase totals sorted descending, so the single largest contributor to frame cost is
/// always TopPhases[0] rather than requiring the caller to sort per-phase totals itself.
/// Intended as an opt-in diagnostic (see SystemManager.Profiler and GameLoop) for tracking down
/// where a gameplay demo's frame budget is actually going, not a permanently-on production
/// feature -- nothing reads it unless a caller explicitly wires an instance in.
/// </summary>
public sealed class PhaseProfiler
{
    private static readonly long TicksBetweenSamples = TimeSpan.TicksPerSecond;

    private readonly Dictionary<string, double> _millisecondsSinceLastSample = [];
    private readonly List<(string Name, double MillisecondsPerSecond)> _topPhases = [];

    private long _lastSampleTicks = DateTime.UtcNow.Ticks;

    /// <summary>Every phase recorded over the last full second, descending by total wall-clock cost -- [0] is always the single largest contributor. Empty until the first full second elapses.</summary>
    public IReadOnlyList<(string Name, double MillisecondsPerSecond)> TopPhases => _topPhases;

    public void Record(string phaseName, TimeSpan elapsed)
    {
        _millisecondsSinceLastSample[phaseName] = _millisecondsSinceLastSample.GetValueOrDefault(phaseName) + elapsed.TotalMilliseconds;

        var currentTicks = DateTime.UtcNow.Ticks;
        if (currentTicks - _lastSampleTicks < TicksBetweenSamples)
        {
            return;
        }

        _topPhases.Clear();
        foreach (var (name, milliseconds) in _millisecondsSinceLastSample)
        {
            _topPhases.Add((name, milliseconds));
        }
        _topPhases.Sort(static (a, b) => b.MillisecondsPerSecond.CompareTo(a.MillisecondsPerSecond));

        _millisecondsSinceLastSample.Clear();
        _lastSampleTicks = currentTicks;
    }
}
