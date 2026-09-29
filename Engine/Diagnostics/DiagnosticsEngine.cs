using Engine.ECS.Context;

namespace Engine.Diagnostics;

/// <summary>Facade over the diagnostics engine's individual feature trackers, each gated by DiagnosticsFeatures.</summary>
/// <remarks>
/// A feature's tracker field stays null when its flag isn't set in Features, so a disabled
/// feature costs nothing beyond the flag check itself. Start subscribes the enabled features to
/// EngineHooks and Dispose unsubscribes them; constructing one touches no process state. Every
/// per-frame step -- the benchmark window, the frame's total cost, startup stability, sampling and
/// reports -- runs from SystemManager's simulation frame hooks, so a host only calls EcsContext.Update.
///
/// FrameBudget and Startup are constructible immediately (they need nothing but the feature
/// flags), so the composition root should construct and start this as early as possible --
/// Startup's clock, and the EngineHooks.DiagnosticScopes it records until the first simulation frame, need to
/// start before any build does. Memory and LeakDetection measure a session's
/// pools, so their trackers are created when a session starts and dropped when it ends; a second
/// session gets fresh ones.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class DiagnosticsEngine : ISimulationFrameListener, ISimulationSessionListener, IDisposable
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(5);

    private readonly string _outputDirectory = DiagnosticsPaths.OutputDirectory;
    private readonly FrameBudgetTracker? _frameBudgetTracker;
    private readonly StartupProfiler? _startupProfiler;
    private readonly FrameRangeBenchmark? _benchmark;
    private readonly IFrameCostRecorder? _frameCostRecorder;
    private readonly List<IDisposable> _hookSubscriptions = [];
    private readonly bool _writesPeriodicReports;
    private ComponentMemoryTracker? _componentMemoryTracker;
    private PoolMemoryReport? _poolMemoryReport;
    private LeakDetector? _leakDetector;
    private EcsContext? _activeSession;
    private IDisposable? _startupScopesSubscription;

    private DateTime _lastReportUtc = DateTime.MinValue;
    private bool _isStarted;

    /// <param name="features">Which features to enable -- opt-in, defaults to None.</param>
    /// <param name="randomSeed">The session's simulation seed, stamped on every report -- see RandomSeed.</param>
    /// <param name="benchmarkFrameRange">Simulation frames to benchmark, or null for none -- see FrameRangeBenchmark. Independent of features: a benchmark records through the same instrumentation whether or not FrameBudget is also on.</param>
    /// <param name="writesPeriodicReports">False for a headless run: no periodic sampling, console ranking or latest.json, which would overwrite a windowed run's. Its reports are the benchmark's.</param>
    public DiagnosticsEngine(DiagnosticsFeatures features, int? randomSeed = null, BenchmarkFrameRange? benchmarkFrameRange = null, bool writesPeriodicReports = true)
    {
        Features = features;
        RandomSeed = randomSeed;
        _writesPeriodicReports = writesPeriodicReports;

        if (features.HasFlag(DiagnosticsFeatures.FrameBudget))
        {
            _frameBudgetTracker = new FrameBudgetTracker();
        }

        if (benchmarkFrameRange is { } range)
        {
            _benchmark = new FrameRangeBenchmark(range);
        }

        _frameCostRecorder = (_frameBudgetTracker, _benchmark) switch
        {
            ({ } tracker, { } benchmark) => new CompositeFrameCostRecorder(tracker, benchmark),
            ({ } tracker, null) => tracker,
            (null, { } benchmark) => benchmark,
            _ => null,
        };

        if (features.HasFlag(DiagnosticsFeatures.Startup))
        {
            _startupProfiler = new StartupProfiler();
        }
    }

    public DiagnosticsFeatures Features { get; }

    /// <summary>
    /// The seed the session being measured was generated from, written into latest.json. Frame
    /// cost depends on what the world is doing -- map layout, which NPCs meet, how fights go --
    /// so two reports are only comparable when they ran the same seed; recording it lets a
    /// consumer (the phase-performance-testing benchmark) verify that rather than assume it.
    /// </summary>
    public int? RandomSeed { get; }

    /// <summary>Subscribes every enabled feature to the EngineHooks channel it listens to.</summary>
    /// <remarks>Frame costs are recorded while FrameBudget is on or a benchmark range was given, feeding both when both are. Simulation frames are heard whenever any feature is on or a benchmark range was given, sessions while Memory or LeakDetection is, and scopes while Startup is, until the first simulation frame.</remarks>
    /// <exception cref="InvalidOperationException">This engine was already started, or another listener holds a channel it needs.</exception>
    public void Start()
    {
        if (_isStarted)
        {
            throw new InvalidOperationException("DiagnosticsEngine was already started.");
        }

        _isStarted = true;

        if (_frameCostRecorder is { } frameCostRecorder)
        {
            _hookSubscriptions.Add(EngineHooks.FrameCosts.Subscribe(frameCostRecorder));
        }

        if (Features != DiagnosticsFeatures.None || _benchmark is not null)
        {
            _hookSubscriptions.Add(EngineHooks.SimulationFrames.Subscribe(this));
        }

        if ((Features & (DiagnosticsFeatures.Memory | DiagnosticsFeatures.LeakDetection)) != DiagnosticsFeatures.None)
        {
            _hookSubscriptions.Add(EngineHooks.Sessions.Subscribe(this));
        }

        if (_startupProfiler is { } startupProfiler)
        {
            _startupScopesSubscription = EngineHooks.DiagnosticScopes.Subscribe(startupProfiler);
        }
    }

    /// <summary>Unsubscribes from every EngineHooks channel Start subscribed to.</summary>
    public void Dispose()
    {
        foreach (var hookSubscription in _hookSubscriptions)
        {
            hookSubscription.Dispose();
        }

        _hookSubscriptions.Clear();
        EndStartupScopes();
    }

    /// <summary>Stops StartupProfiler recording scopes: what builds after the first simulation frame (a staging rebuild) is not startup.</summary>
    private void EndStartupScopes()
    {
        _startupScopesSubscription?.Dispose();
        _startupScopesSubscription = null;
    }

    /// <summary>True once a benchmark range was given and its report has been written -- a headless run's signal to stop.</summary>
    /// <remarks>Becomes true as frame EndFrame - 1 ends, so a host that updates until it is true simulates exactly the frames before EndFrame.</remarks>
    public bool IsBenchmarkComplete => _benchmark?.IsComplete ?? false;

    /// <summary>The single largest frame-cost contributor, for a live on-screen readout (see DebugWindowContent). Null unless FrameBudget is enabled or no full second has sampled yet.</summary>
    public (string Name, double MillisecondsPerSecond)? TopFrameCostEntry =>
        _frameBudgetTracker?.TopEntries is { Count: > 0 } entries ? entries[0] : null;

    /// <summary>Creates Memory's and LeakDetection's trackers over the session's pools.</summary>
    /// <exception cref="InvalidOperationException">Another session is still active -- one simulated session at a time.</exception>
    void ISimulationSessionListener.SessionStarted(EcsContext session)
    {
        if (_activeSession is not null)
        {
            throw new InvalidOperationException("A simulation session started while another is still active; end the first by disposing its EcsContext.");
        }

        _activeSession = session;
        var componentManager = session.ComponentManager;
        var entityManager = session.EntityManager;

        if (Features.HasFlag(DiagnosticsFeatures.Memory))
        {
            _componentMemoryTracker = new ComponentMemoryTracker(componentManager);

            if (_benchmark is { IsComplete: false })
            {
                _poolMemoryReport = new PoolMemoryReport(componentManager, entityManager);
            }
        }

        if (Features.HasFlag(DiagnosticsFeatures.LeakDetection))
        {
            _leakDetector = new LeakDetector(entityManager, componentManager);
        }
    }

    /// <summary>Drops the session's trackers, so nothing keeps measuring -- or holding -- pools that are going away.</summary>
    /// <remarks>A pool memory report whose range opened but never closed is dropped, not written: it would claim the whole range.</remarks>
    /// <exception cref="InvalidOperationException">session is not the active session.</exception>
    void ISimulationSessionListener.SessionEnding(EcsContext session)
    {
        if (!ReferenceEquals(_activeSession, session))
        {
            throw new InvalidOperationException("A simulation session ended that is not the active one.");
        }

        if (_poolMemoryReport is { HasBaseline: true } && _benchmark is { IsComplete: false } benchmark)
        {
            Console.WriteLine($"[Memory] Session ended before frame {benchmark.Range.EndFrame}; no memory report written.");
        }

        _activeSession = null;
        _componentMemoryTracker = null;
        _poolMemoryReport = null;
        _leakDetector = null;
    }

    /// <summary>Ends startup's scope recording at the first frame, and opens the benchmark range as its first frame starts.</summary>
    /// <remarks>With Memory on, PoolMemoryReport's baseline copy is taken first, before the benchmark's clock starts.</remarks>
    void ISimulationFrameListener.SimulationFrameStarting(long frameCount)
    {
        EndStartupScopes();

        if (_benchmark is not { IsComplete: false } benchmark)
        {
            return;
        }

        if (_poolMemoryReport is { HasBaseline: false } poolMemoryReport && !benchmark.IsRecording && frameCount >= benchmark.Range.StartFrame && frameCount < benchmark.Range.EndFrame)
        {
            poolMemoryReport.CaptureBaseline();
        }

        benchmark.SimulationFrameStarting(frameCount);
    }

    /// <summary>
    /// Records the frame's whole cost as "GameLoop" / "EcsContext.Update (all systems)", feeds it to
    /// StartupProfiler's stability detection until stable, closes the benchmark range after its last
    /// frame, then samples and reports on the periodic cadence.
    /// </summary>
    /// <remarks>StartupProfiler needs this measured cost rather than the gap between frames -- see StartupProfiler.Tick.</remarks>
    void ISimulationFrameListener.SimulationFrameEnded(long frameCount, TimeSpan elapsed)
    {
        _frameCostRecorder?.Record(FrameCostCategory.Update, "GameLoop", "EcsContext.Update (all systems)", elapsed);

        if (_startupProfiler is { IsStable: false } startupProfiler)
        {
            startupProfiler.Tick(elapsed);
            if (startupProfiler.IsStable)
            {
                startupProfiler.WriteReport(_outputDirectory);
            }
        }

        if (_benchmark is { IsComplete: false } benchmark)
        {
            benchmark.SimulationFrameEnded(frameCount);
            if (benchmark.IsComplete)
            {
                WriteBenchmarkReports(benchmark);
            }
        }

        if (_writesPeriodicReports)
        {
            SampleAndReport();
        }
    }

    private void WriteBenchmarkReports(FrameRangeBenchmark benchmark)
    {
        var path = benchmark.WriteReport(_outputDirectory, RandomSeed);
        Console.WriteLine($"[Benchmark] Frames {benchmark.Range.StartFrame}-{benchmark.Range.EndFrame} written to {path}");

        if (_poolMemoryReport is { HasBaseline: true } completedMemoryReport)
        {
            var memoryPath = completedMemoryReport.WriteReport(_outputDirectory, RandomSeed, benchmark.Range);
            Console.WriteLine($"[Memory] Frames {benchmark.Range.StartFrame}-{benchmark.Range.EndFrame} written to {memoryPath}");
        }
    }

    /// <summary>
    /// Drives every enabled feature's own throttled sampling, and -- on its own ~5s cadence,
    /// independent of the frame count -- writes Log/diagnostics/latest.json|txt and prints the
    /// frame-cost ranking to the console.
    /// </summary>
    /// <remarks>Runs as each simulation frame ends, so sampling and reports pause with the simulation.</remarks>
    private void SampleAndReport()
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

        DiagnosticsReportWriter.Write(_outputDirectory, Features, RandomSeed, _frameBudgetTracker?.Snapshot, _componentMemoryTracker?.Snapshot, _leakDetector?.Findings);
    }
}
