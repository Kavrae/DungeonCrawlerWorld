using System.Diagnostics;
using System.Text.Json;

namespace Engine.Diagnostics;

/// <summary>Records wall-clock cost of named startup phases, plus wall-clock time from construction until frame pacing stabilizes.</summary>
/// <remarks>
/// Phase(name) is a Stopwatch-backed scope -- `using var _ = startupProfiler?.Phase("Module Load")`
/// around each major startup step (see GameLoop.Initialize, Bootstrapper.Build,
/// GameBootstrapper.Build). Phases are recorded in call order, one entry per call -- unlike
/// FrameBudgetTracker, nothing repeats every frame here, so there's nothing to aggregate.
///
/// Tick() is called once per frame from GameLoop.Update while IsStable is false. It measures the
/// actual wall-clock gap since the previous Tick() (not the simulated/fixed GameTime step) and
/// keeps a rolling window of the most recent gaps; once their spread narrows and stays narrow for
/// several windows in a row, IsStable flips true and TimeToStable is recorded -- this answers
/// "time until stable," not just "time until Initialize() returns" (real steady state settles
/// well after Initialize, once JIT/GC warmup finishes). Once stable, Tick() becomes a no-op, so
/// this costs nothing for the rest of a long session.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class StartupProfiler
{
    private const int WindowSizeFrames = 120;
    private const double CoefficientOfVariationThreshold = 0.15;
    private const int RequiredConsecutiveStableWindows = 5;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly List<PhaseRecord> _phases = [];
    private readonly double[] _recentFrameMilliseconds = new double[WindowSizeFrames];
    private readonly long _constructedTimestamp = Stopwatch.GetTimestamp();

    private int _frameSampleCount;
    private int _nextSampleIndex;
    private int _consecutiveStableWindows;

    /// <summary>Every phase recorded so far, in call order.</summary>
    public IReadOnlyList<PhaseRecord> Phases => _phases;

    /// <summary>True once frame pacing has stayed comfortably steady for RequiredConsecutiveStableWindows windows in a row.</summary>
    public bool IsStable { get; private set; }

    /// <summary>Wall-clock time from this profiler's construction until IsStable first became true. Null until then.</summary>
    public TimeSpan? TimeToStable { get; private set; }

    /// <summary>Starts timing a named phase; disposing the result records its elapsed time.</summary>
    public IDisposable Phase(string name) => new PhaseScope(this, name);

    /// <summary>
    /// Feeds one frame's actual simulation work cost into the stability detector. No-op once
    /// IsStable. Callers must pass real measured work (e.g. GameLoop's own
    /// Stopwatch.GetElapsedTime bracket around EcsContext.Update), not a raw gap between Tick
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
            _phases.ConvertAll(static phase => new StartupReportPhase(phase.Name, phase.Milliseconds)));

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

    private void RecordPhase(string name, TimeSpan elapsed) => _phases.Add(new PhaseRecord(name, elapsed.TotalMilliseconds));

    private sealed class PhaseScope(StartupProfiler owner, string name) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.RecordPhase(name, Stopwatch.GetElapsedTime(_start));
        }
    }

    private sealed record StartupReport(DateTime TimestampUtc, bool IsStable, double? TimeToStableMilliseconds, List<StartupReportPhase> Phases);

    private sealed record StartupReportPhase(string Name, double Milliseconds);
}
