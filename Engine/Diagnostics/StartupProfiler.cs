using System.Diagnostics;
using System.Text.Json;

namespace Engine.Diagnostics;

/// <summary>Records wall-clock cost of named startup phases, plus wall-clock time from construction until frame pacing stabilizes.</summary>
/// <remarks>
/// Its phases are the EngineHooks.DiagnosticScopes emitted while it listens -- the host's startup steps,
/// EcsBuilder's stages and each module's phase within them, mod trial builds -- which
/// DiagnosticsEngine subscribes it to until the first simulation frame starts. Phases are recorded
/// in start order with their nesting depth, one entry per scope -- unlike FrameBudgetTracker,
/// nothing repeats every frame here, so there's nothing to aggregate.
///
/// Tick(elapsed) is fed each simulation frame's measured cost by DiagnosticsEngine, from the
/// EngineHooks.SimulationFrames hook, while IsStable is false. It keeps a rolling window of the
/// most recent costs; once their spread narrows and stays narrow for
/// several windows in a row, IsStable flips true and TimeToStable is recorded -- this answers
/// "time until stable," not just "time until Initialize() returns" (real steady state settles
/// well after Initialize, once JIT/GC warmup finishes). Once stable, Tick() becomes a no-op, so
/// this costs nothing for the rest of a long session.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class StartupProfiler : IDiagnosticScopeListener
{
    private const int WindowSizeFrames = 120;
    private const double CoefficientOfVariationThreshold = 0.15;
    private const int RequiredConsecutiveStableWindows = 5;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly List<PhaseRecord> _phases = [];
    private readonly Stack<int> _openPhaseIndexes = new();
    private readonly double[] _recentFrameMilliseconds = new double[WindowSizeFrames];
    private readonly long _constructedTimestamp = Stopwatch.GetTimestamp();

    private int _frameSampleCount;
    private int _nextSampleIndex;
    private int _consecutiveStableWindows;

    /// <summary>Every phase recorded so far, in start order; a phase still open reads 0 ms.</summary>
    public IReadOnlyList<PhaseRecord> Phases => _phases;

    /// <summary>True once frame pacing has stayed comfortably steady for RequiredConsecutiveStableWindows windows in a row.</summary>
    public bool IsStable { get; private set; }

    /// <summary>Wall-clock time from this profiler's construction until IsStable first became true. Null until then.</summary>
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

        RecordFrameSample(elapsed.TotalMilliseconds);
    }

    /// <summary>Writes the current phase list and stability result to outputDirectory as a one-shot startup-&lt;timestamp&gt;.json.</summary>
    public void WriteReport(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var report = new StartupReport(
            DateTime.UtcNow,
            IsStable,
            TimeToStable?.TotalMilliseconds,
            _phases.ConvertAll(static phase => new StartupReportPhase(phase.Name, phase.Milliseconds, phase.Depth)));

        var fileName = $"startup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
        File.WriteAllText(Path.Combine(outputDirectory, fileName), JsonSerializer.Serialize(report, JsonOptions));
    }

    private void RecordFrameSample(double milliseconds)
    {
        _recentFrameMilliseconds[_nextSampleIndex] = milliseconds;
        _nextSampleIndex = (_nextSampleIndex + 1) % WindowSizeFrames;

        if (_frameSampleCount < WindowSizeFrames)
        {
            _frameSampleCount++;
            return;
        }

        var sum = 0.0;
        for (var i = 0; i < WindowSizeFrames; i++)
        {
            sum += _recentFrameMilliseconds[i];
        }

        var mean = sum / WindowSizeFrames;
        if (mean <= 0)
        {
            return;
        }

        var sumOfSquaredDeviations = 0.0;
        for (var i = 0; i < WindowSizeFrames; i++)
        {
            var deviation = _recentFrameMilliseconds[i] - mean;
            sumOfSquaredDeviations += deviation * deviation;
        }

        var coefficientOfVariation = System.Math.Sqrt(sumOfSquaredDeviations / WindowSizeFrames) / mean;

        _consecutiveStableWindows = coefficientOfVariation <= CoefficientOfVariationThreshold
            ? _consecutiveStableWindows + 1
            : 0;

        if (_consecutiveStableWindows >= RequiredConsecutiveStableWindows)
        {
            IsStable = true;
            TimeToStable = Stopwatch.GetElapsedTime(_constructedTimestamp);
        }
    }

    private sealed record StartupReport(DateTime TimestampUtc, bool IsStable, double? TimeToStableMilliseconds, List<StartupReportPhase> Phases);

    private sealed record StartupReportPhase(string Name, double Milliseconds, int Depth);
}
