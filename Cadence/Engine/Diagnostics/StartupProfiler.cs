using System.Diagnostics;
using System.Text.Json;

namespace Engine.Diagnostics;

/// <summary>Records wall-clock cost of named startup phases, plus wall-clock time from construction until the simulation's frame cost settles.</summary>
/// <remarks>
/// Its phases are the EngineHooks.DiagnosticScopes emitted while it listens -- the host's startup steps,
/// EcsBuilder's stages and each module's phase within them, mod trial builds -- which
/// DiagnosticsEngine subscribes it to until the first simulation frame starts, when the phase list is final and
/// DiagnosticsEngine writes it, so a run that never settles still keeps it. Phases are recorded
/// in start order with their nesting depth, one entry per scope -- unlike FrameBudgetTracker,
/// nothing repeats every frame here, so there's nothing to aggregate.
///
/// Tick(elapsed) is fed each simulation frame's measured cost by DiagnosticsEngine while IsStable is false. The costs
/// are split into back-to-back windows of WindowFrameCount frames, each summarized by its median, so a GC pause or a
/// streaming hitch moves one window's median little and never the next one's. Frame cost is settled once
/// RequiredAgreeingWindows windows in a row have medians within MedianSpreadThreshold of their mean; TimeToStable is
/// when the first of them began -- when the cost settled, not when that was confirmed. That answers "time until
/// stable," not just "time until Initialize() returns" (real steady state settles well after Initialize, once JIT/GC
/// warmup finishes). Once stable, Tick() is a no-op, so this costs nothing for the rest of a long session.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class StartupProfiler : IDiagnosticScopeListener
{
    /// <summary>Frames in each window: one second at 60 frames per second.</summary>
    public const int WindowFrameCount = 60;

    /// <summary>How many windows in a row must agree.</summary>
    public const int RequiredAgreeingWindows = 5;

    /// <summary>How far apart, as a fraction of their mean, the agreeing windows' medians may be.</summary>
    public const double MedianSpreadThreshold = 0.15;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly List<PhaseRecord> _phases = [];
    private readonly Stack<int> _openPhaseIndexes = new();
    private readonly double[] _windowFrameMilliseconds = new double[WindowFrameCount];
    private readonly double[] _recentWindowMedians = new double[RequiredAgreeingWindows];
    private readonly long[] _recentWindowStartTimestamps = new long[RequiredAgreeingWindows];
    private readonly long _constructedTimestamp = Stopwatch.GetTimestamp();

    private int _windowSampleCount;
    private long _windowStartTimestamp;
    private int _closedWindowCount;
    private string? _reportPath;

    /// <summary>Every phase recorded so far, in start order; a phase still open reads 0 ms.</summary>
    public IReadOnlyList<PhaseRecord> Phases => _phases;

    /// <summary>True once RequiredAgreeingWindows windows in a row have had medians within MedianSpreadThreshold of each other.</summary>
    public bool IsStable { get; private set; }

    /// <summary>Wall-clock time from this profiler's construction until the first of the agreeing windows began. Null until IsStable.</summary>
    public TimeSpan? TimeToStable { get; private set; }

    /// <summary>Records scope as a phase, nested inside whichever phase is still open.</summary>
    public void ScopeStarted(in DiagnosticScope scope)
    {
        _openPhaseIndexes.Push(_phases.Count);
        _phases.Add(new PhaseRecord(scope.ToString(), Milliseconds: 0, Depth: _openPhaseIndexes.Count - 1));
    }

    /// <summary>Fills in the innermost open phase's duration.</summary>
    public void ScopeEnded(in DiagnosticScope scope, TimeSpan elapsed)
    {
        var phaseIndex = _openPhaseIndexes.Pop();
        _phases[phaseIndex] = _phases[phaseIndex] with { Milliseconds = elapsed.TotalMilliseconds };
    }

    /// <summary>
    /// Feeds one frame's actual simulation work cost into the stability detector. No-op once
    /// IsStable. Callers must pass real measured work (DiagnosticsEngine passes SystemManager.Update's
    /// own measured cost from the SimulationFrames hook), not a raw gap between Tick
    /// calls -- MonoGame's fixed-timestep loop pins that gap to the target frame rate as long as
    /// per-frame work stays under budget, which makes "time between calls" look stable almost
    /// immediately even while the real per-frame cost underneath is still climbing during JIT/GC
    /// warmup.
    /// </summary>
    public void Tick(TimeSpan elapsed)
    {
        if (IsStable)
        {
            return;
        }

        if (_windowSampleCount == 0)
        {
            _windowStartTimestamp = Stopwatch.GetTimestamp();
        }

        _windowFrameMilliseconds[_windowSampleCount++] = elapsed.TotalMilliseconds;
        if (_windowSampleCount == WindowFrameCount)
        {
            CloseWindow();
        }
    }

    /// <summary>Writes the current phase list and stability result to outputDirectory as startup-&lt;timestamp&gt;.json, the same file every later call rewrites.</summary>
    /// <returns>The JSON it wrote.</returns>
    public string WriteReport(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var report = new DiagnosticsStartupReport(
            DateTime.UtcNow,
            IsStable,
            TimeToStable?.TotalMilliseconds,
            [.. _phases]);

        var reportJson = JsonSerializer.Serialize(report, JsonOptions);
        _reportPath ??= Path.Combine(outputDirectory, $"startup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(_reportPath, reportJson);
        return reportJson;
    }

    private void CloseWindow()
    {
        Array.Sort(_windowFrameMilliseconds);
        var middleIndex = WindowFrameCount / 2;
        var median = WindowFrameCount % 2 == 1
            ? _windowFrameMilliseconds[middleIndex]
            : (_windowFrameMilliseconds[middleIndex - 1] + _windowFrameMilliseconds[middleIndex]) / 2;

        Array.Copy(_recentWindowMedians, 1, _recentWindowMedians, 0, RequiredAgreeingWindows - 1);
        Array.Copy(_recentWindowStartTimestamps, 1, _recentWindowStartTimestamps, 0, RequiredAgreeingWindows - 1);
        _recentWindowMedians[^1] = median;
        _recentWindowStartTimestamps[^1] = _windowStartTimestamp;
        _windowSampleCount = 0;
        _closedWindowCount++;

        if (_closedWindowCount < RequiredAgreeingWindows)
        {
            return;
        }

        var lowestMedian = _recentWindowMedians.Min();
        var highestMedian = _recentWindowMedians.Max();
        var meanMedian = _recentWindowMedians.Average();
        if (meanMedian <= 0 || (highestMedian - lowestMedian) / meanMedian > MedianSpreadThreshold)
        {
            return;
        }

        IsStable = true;
        TimeToStable = Stopwatch.GetElapsedTime(_constructedTimestamp, _recentWindowStartTimestamps[0]);
    }
}
