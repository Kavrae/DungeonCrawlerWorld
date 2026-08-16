using Engine.ECS.Components;
using Engine.ECS.Entities;

namespace Engine.Diagnostics;

/// <summary>Facade over the diagnostics engine's individual feature trackers, each gated by DiagnosticsFeatures.</summary>
/// <remarks>
/// A feature's tracker field stays null when its flag isn't set in Features, so a disabled
/// feature costs nothing beyond the flag check itself -- mirrors SystemManager.Profiler/
/// EventBus.Profiler's own null-means-off idiom, just centralized here as the single place a
/// composition root (GameLoop) needs to construct and wire.
///
/// FrameBudget and Startup are constructible immediately (they need nothing but the feature
/// flags), so the composition root should construct this as early as possible -- Startup's own
/// clock, and its Phase("...") scopes around the composition root's own early steps, need to
/// start before ComponentManager/EntityManager exist. Memory and LeakDetection need those, so
/// they're deferred to AttachEcsContext, called once they're available.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class DiagnosticsEngine
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(5);

    private readonly string _outputDirectory = DiagnosticsPaths.OutputDirectory;
    private readonly FrameBudgetTracker? _frameBudgetTracker;
    private readonly StartupProfiler? _startupProfiler;
    private ComponentMemoryTracker? _componentMemoryTracker;
    private LeakDetector? _leakDetector;

    private DateTime _lastReportUtc = DateTime.MinValue;

    /// <param name="features">Which features to enable -- opt-in, defaults to None.</param>
    public DiagnosticsEngine(DiagnosticsFeatures features)
    {
        Features = features;

        if (features.HasFlag(DiagnosticsFeatures.FrameBudget))
        {
            _frameBudgetTracker = new FrameBudgetTracker();
        }

        if (features.HasFlag(DiagnosticsFeatures.Startup))
        {
            _startupProfiler = new StartupProfiler();
        }
    }

    public DiagnosticsFeatures Features { get; }

    /// <summary>Null unless DiagnosticsFeatures.FrameBudget is enabled -- wire into SystemManager.Profiler/EventBus.Profiler/GameShellContext when non-null.</summary>
    public IFrameCostRecorder? FrameCostRecorder => _frameBudgetTracker;

    /// <summary>Null unless DiagnosticsFeatures.Startup is enabled -- wrap the composition root's own early steps with `using var _ = diagnostics.StartupProfiler?.Phase("...")`, and thread it into Bootstrapper.Build/GameBootstrapper.Build for their own per-module phases.</summary>
    public StartupProfiler? StartupProfiler => _startupProfiler;

    /// <summary>The single largest frame-cost contributor, for a live on-screen readout (see DebugWindowContent). Null unless FrameBudget is enabled or no full second has sampled yet.</summary>
    public (string Name, double MillisecondsPerSecond)? TopFrameCostEntry =>
        _frameBudgetTracker?.TopEntries is { Count: > 0 } entries ? entries[0] : null;

    /// <summary>
    /// Constructs Memory/LeakDetection's trackers once ComponentManager/EntityManager exist --
    /// they can't exist at construction time (see this class's own remarks). No-op for any flag
    /// not set in Features, and safe to call more than once (only constructs a tracker the first
    /// time).
    /// </summary>
    public void AttachEcsContext(ComponentManager componentManager, EntityManager entityManager)
    {
        ArgumentNullException.ThrowIfNull(componentManager);
        ArgumentNullException.ThrowIfNull(entityManager);

        if (Features.HasFlag(DiagnosticsFeatures.Memory) && _componentMemoryTracker is null)
        {
            _componentMemoryTracker = new ComponentMemoryTracker(componentManager);
        }

        if (Features.HasFlag(DiagnosticsFeatures.LeakDetection) && _leakDetector is null)
        {
            _leakDetector = new LeakDetector(entityManager, componentManager);
        }
    }

    /// <summary>
    /// Records one EcsContext.Update tick's elapsed cost under FrameCostCategory.Update, and --
    /// while Startup is enabled and not yet stable -- feeds the same measured cost into
    /// StartupProfiler's stability detection, auto-writing its one-shot report the moment it
    /// becomes stable. One call, because both are facets of the same event (the composition
    /// root's own aggregate per-tick simulation cost) -- see StartupProfiler.Tick's own doc
    /// comment for why it specifically needs this measured cost, not a raw gap between calls.
    /// </summary>
    public void RecordSimulationTick(string groupName, string itemName, TimeSpan elapsed)
    {
        _frameBudgetTracker?.Record(FrameCostCategory.Update, groupName, itemName, elapsed);

        if (_startupProfiler is { IsStable: false } startupProfiler)
        {
            startupProfiler.Tick(elapsed);
            if (startupProfiler.IsStable)
            {
                startupProfiler.WriteReport(_outputDirectory);
            }
        }
    }

    /// <summary>
    /// Drives every enabled feature's own throttled sampling, and -- on its own ~5s cadence,
    /// independent of the caller's frame count -- writes Log/diagnostics/latest.json|txt and
    /// prints the frame-cost ranking to the console. Call once per frame from the composition
    /// root's Update; every decision about *when* to actually report lives here, not in the
    /// caller, so a composition root (GameLoop or otherwise) just calls this and stays out of
    /// the reporting business entirely.
    /// </summary>
    public void Tick()
    {
        _componentMemoryTracker?.Tick();
        _leakDetector?.Tick();

        var now = DateTime.UtcNow;
        if (now - _lastReportUtc < ReportInterval)
        {
            return;
        }

        _lastReportUtc = now;

        ReportFrameBudgetToConsole();
        WriteReports();
    }

    /// <summary>Dumps the full last-second frame-cost ranking to the console -- a single on-screen "Top: X" readout (see DebugWindowContent) is enough to notice a hotspot while playing, but this keeps a fuller trail (the #2, #3, ... contributors too) for after a demo ends.</summary>
    private void ReportFrameBudgetToConsole()
    {
        if (_frameBudgetTracker?.Snapshot is not { Count: > 0 } snapshot)
        {
            return;
        }

        Console.WriteLine("[PerformanceProfile] Top costs (ms spent in the last second):");
        foreach (var entry in snapshot)
        {
            Console.WriteLine($"[PerformanceProfile]   [{entry.Category}] {entry.GroupName}.{entry.ItemName}: {entry.MillisecondsPerSecond:N1}ms");
        }
    }

    private void WriteReports()
    {
        if (Features == DiagnosticsFeatures.None)
        {
            return;
        }

        DiagnosticsReportWriter.Write(_outputDirectory, Features, _frameBudgetTracker?.Snapshot, _componentMemoryTracker?.Snapshot, _leakDetector?.Findings);
    }
}
