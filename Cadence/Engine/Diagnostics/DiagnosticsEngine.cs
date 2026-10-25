using Engine.ECS.Context;
using System.Diagnostics;
using System.Net;

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
/// start before any build does. Memory, LeakDetection and Gauges measure a
/// session, so their trackers are created when a session starts and dropped when it ends; a second
/// session gets fresh ones.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class DiagnosticsEngine : ISimulationFrameListener, ISimulationSessionListener, IHostFrameListener, IDisposable
{
    private const string FrameGroupName = "GameLoop";
    private const string FrameItemName = "EcsContext.Update (all systems)";

    private static readonly long ReportIntervalTimestampTicks = Stopwatch.Frequency * 5;

    private readonly string _outputDirectory = DiagnosticsPaths.OutputDirectory;
    private readonly FrameBudgetTracker? _frameBudgetTracker;
    private readonly StartupProfiler? _startupProfiler;
    private readonly FrameRangeBenchmark? _benchmark;
    private readonly IFrameCostRecorder? _frameCostRecorder;
    private readonly DiagnosticsLink? _link;
    private readonly List<IDisposable> _hookSubscriptions = [];
    private readonly bool _writesPeriodicReports;
    private ComponentMemoryTracker? _componentMemoryTracker;
    private PoolMemoryReport? _poolMemoryReport;
    private LeakDetector? _leakDetector;
    private GaugeTracker? _gaugeTracker;
    private GaugeSeriesReport? _gaugeSeriesReport;
    private GaugeHistory? _gaugeHistory;
    private EcsContext? _activeSession;
    private IDisposable? _startupScopesSubscription;

    private long _lastReportTimestamp;
    private bool _hasReported;

    /// <summary>The last periodic report, written off the simulation thread; a new one starts only once it has finished.</summary>
    private Task _pendingReport = Task.CompletedTask;
    private bool _isStarted;

    /// <param name="features">Which features to enable -- opt-in, defaults to None.</param>
    /// <param name="randomSeed">The session's simulation seed, stamped on every report -- see RandomSeed.</param>
    /// <param name="benchmarkFrameRange">Simulation frames to benchmark, or null for none -- see FrameRangeBenchmark. Independent of features: a benchmark records through the same instrumentation whether or not FrameBudget is also on.</param>
    /// <param name="writesPeriodicReports">False for a headless run: no periodic sampling, console ranking or latest.json, which would overwrite a windowed run's. Its reports are the benchmark's.</param>
    /// <param name="linkEndPoint">Where an IDE listens for this run's diagnostics, or null for no link -- see DiagnosticsLink and DiagnosticsLinkArgument.</param>
    public DiagnosticsEngine(DiagnosticsFeatures features, int? randomSeed = null, BenchmarkFrameRange? benchmarkFrameRange = null, bool writesPeriodicReports = true, IPEndPoint? linkEndPoint = null)
    {
        Features = features;
        RandomSeed = randomSeed;
        _writesPeriodicReports = writesPeriodicReports;

        if (linkEndPoint is not null)
        {
            _link = new DiagnosticsLink(linkEndPoint, features, randomSeed, Console.Error);
        }

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
    /// <remarks>Frame costs are recorded while FrameBudget is on or a benchmark range was given, feeding both when both are. Simulation frames are heard whenever any feature is on, a benchmark range was given or a link was, sessions while Memory, LeakDetection or Gauges is or a link was, host frames while a link was, and scopes while Startup is, until the first simulation frame. A link starts connecting here.</remarks>
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

        if (Features != DiagnosticsFeatures.None || _benchmark is not null || _link is not null)
        {
            _hookSubscriptions.Add(EngineHooks.SimulationFrames.Subscribe(this));
        }

        if ((Features & (DiagnosticsFeatures.Memory | DiagnosticsFeatures.LeakDetection | DiagnosticsFeatures.Gauges)) != DiagnosticsFeatures.None || _link is not null)
        {
            _hookSubscriptions.Add(EngineHooks.Sessions.Subscribe(this));
        }

        if (_link is { } link)
        {
            _hookSubscriptions.Add(EngineHooks.HostFrames.Subscribe(this));
            link.Start();
        }

        if (_startupProfiler is { } startupProfiler)
        {
            _startupScopesSubscription = EngineHooks.DiagnosticScopes.Subscribe(startupProfiler);
        }
    }

    /// <summary>Unsubscribes from every EngineHooks channel Start subscribed to, waits for a periodic report still being written, and closes the link once it has sent what it holds.</summary>
    public void Dispose()
    {
        foreach (var hookSubscription in _hookSubscriptions)
        {
            hookSubscription.Dispose();
        }

        _hookSubscriptions.Clear();
        EndStartupScopes();
        _pendingReport.Wait();
        _link?.Dispose();
    }

    /// <summary>True once a link was given and has closed -- it failed, the IDE went away, or this engine was disposed.</summary>
    public bool IsLinkClosed => _link?.IsClosed ?? false;

    /// <summary>Stops StartupProfiler recording scopes: what builds after the first simulation frame (a staging rebuild) is not startup.</summary>
    private void EndStartupScopes()
    {
        _startupScopesSubscription?.Dispose();
        _startupScopesSubscription = null;
    }

    /// <summary>True once a benchmark range was given and its report has been written -- a headless run's signal to stop.</summary>
    /// <remarks>Becomes true as frame EndFrame - 1 ends, so a host that updates until it is true simulates exactly the frames before EndFrame.</remarks>
    public bool IsBenchmarkComplete => _benchmark?.IsComplete ?? false;

    /// <summary>The single largest frame-cost contributor, for a live on-screen readout. Null unless FrameBudget is enabled or no full second has sampled yet.</summary>
    public (string Name, double MillisecondsPerSecond)? TopFrameCostEntry =>
        _frameBudgetTracker?.TopEntries is { Count: > 0 } entries ? entries[0] : null;

    /// <summary>The active session's gauges as last sampled; null unless Gauges is on and a session is active.</summary>
    public GaugeTracker? Gauges => _gaugeTracker;

    /// <summary>The active session's last GaugeHistory.HistoryFrameCount frames of gauges, for a live display; null unless Gauges is on and a session is active.</summary>
    public GaugeHistory? LiveGauges => _gaugeHistory;

    /// <summary>The leak detector's current findings; null unless LeakDetection is on and a session is active.</summary>
    public IReadOnlyList<LeakFinding>? LeakFindings => _leakDetector?.Findings;

    /// <summary>Creates Memory's and LeakDetection's trackers over the session's pools, and Gauges' over its gauges -- with a series report when a benchmark range has yet to open.</summary>
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
            _componentMemoryTracker = new ComponentMemoryTracker(componentManager, session.EntityPopulations);

            if (_benchmark is { IsComplete: false })
            {
                _poolMemoryReport = new PoolMemoryReport(componentManager, entityManager);
            }
        }

        if (Features.HasFlag(DiagnosticsFeatures.LeakDetection))
        {
            _leakDetector = new LeakDetector(entityManager, componentManager, session.EntityPopulations);
        }

        if (Features.HasFlag(DiagnosticsFeatures.Gauges))
        {
            _gaugeTracker = new GaugeTracker([.. GaugeTracker.ProcessGauges, .. session.Gauges.Gauges]);

            _gaugeHistory = new GaugeHistory(_gaugeTracker);

            if (_benchmark is { IsComplete: false, IsRecording: false } benchmark)
            {
                _gaugeSeriesReport = new GaugeSeriesReport(_gaugeTracker, benchmark.Range);
            }
        }

        _link?.SessionStarted(_gaugeTracker?.Gauges ?? []);
    }

    /// <summary>Drops the session's trackers, so nothing keeps measuring -- or holding -- pools that are going away.</summary>
    /// <remarks>A pool memory or gauge report whose range opened but never closed is dropped, not written: it would claim the whole range.</remarks>
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

        if (_gaugeSeriesReport is { RecordedFrameCount: > 0, IsComplete: false } unfinishedGaugeSeriesReport)
        {
            Console.WriteLine($"[Gauges] Session ended before frame {unfinishedGaugeSeriesReport.Range.EndFrame}; no gauge report written.");
        }

        _link?.SessionEnded();
        _activeSession = null;
        _componentMemoryTracker = null;
        _poolMemoryReport = null;
        _leakDetector = null;
        _gaugeTracker = null;
        _gaugeSeriesReport = null;
        _gaugeHistory = null;
    }

    /// <summary>Hands the host's update to the link, which counts the update rate and notices a pause.</summary>
    void IHostFrameListener.HostUpdated(bool simulated) => _link?.HostUpdated(simulated);

    /// <summary>Hands the host's draw to the link, which counts the draw rate.</summary>
    void IHostFrameListener.HostDrew() => _link?.HostDrew();

    /// <summary>Ends startup's scope recording at the first frame, and opens the benchmark range as its first frame starts.</summary>
    /// <remarks>With Memory on, PoolMemoryReport's baseline copy is taken first, before the benchmark's clock starts.</remarks>
    void ISimulationFrameListener.SimulationFrameStarting(long frameCount)
    {
        EndStartupScopes();
        _frameCostRecorder?.Begin(FrameCostCategory.Update, FrameGroupName, FrameItemName);

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
    /// Records the frame's whole cost as "GameLoop" / "EcsContext.Update (all systems)", samples the gauges and records them on the link, feeds that cost
    /// to StartupProfiler's stability detection until stable, closes the benchmark range after its last
    /// frame, then samples and reports on the periodic cadence.
    /// </summary>
    /// <remarks>StartupProfiler needs this measured cost rather than the gap between frames -- see StartupProfiler.Tick.</remarks>
    void ISimulationFrameListener.SimulationFrameEnded(long frameCount, TimeSpan elapsed)
    {
        _frameCostRecorder?.Record(FrameCostCategory.Update, FrameGroupName, FrameItemName, elapsed);
        _gaugeTracker?.Sample();
        _gaugeSeriesReport?.Record(frameCount, elapsed);
        _gaugeHistory?.Record(frameCount, elapsed);
        _link?.RecordFrame(frameCount, elapsed, _gaugeTracker);

        if (_startupProfiler is { IsStable: false } startupProfiler)
        {
            startupProfiler.Tick(elapsed);
            if (startupProfiler.IsStable)
            {
                var startupReportJson = startupProfiler.WriteReport(_outputDirectory);
                _link?.SendStartupReport(startupReportJson);
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

        string? memoryPath = null;
        if (_poolMemoryReport is { HasBaseline: true } completedMemoryReport)
        {
            memoryPath = completedMemoryReport.WriteReport(_outputDirectory, RandomSeed, benchmark.Range);
            Console.WriteLine($"[Memory] Frames {benchmark.Range.StartFrame}-{benchmark.Range.EndFrame} written to {memoryPath}");
        }

        string? gaugesPath = null;
        if (_gaugeSeriesReport is { IsComplete: true } completedGaugeSeriesReport)
        {
            gaugesPath = completedGaugeSeriesReport.WriteReport(_outputDirectory, RandomSeed);
            Console.WriteLine($"[Gauges] Frames {benchmark.Range.StartFrame}-{benchmark.Range.EndFrame} written to {gaugesPath}");
        }

        _gaugeSeriesReport = null;
        _link?.SendBenchmarkCompleted(new DiagnosticsLinkBenchmarkCompletion(benchmark.Range.StartFrame, benchmark.Range.EndFrame, path, memoryPath, gaugesPath));
    }

    /// <summary>
    /// Drives every enabled feature's own throttled sampling, and -- on its own ~5s cadence,
    /// independent of the frame count -- writes Log/diagnostics/latest.json|txt and prints the
    /// frame-cost ranking to the console.
    /// </summary>
    /// <remarks>
    /// Runs as each simulation frame ends, so sampling and reports pause with the simulation. The snapshots are copied
    /// here and written by a background task, so the file and console writes never land in a simulation frame; an
    /// interval whose previous report is still being written is skipped rather than queued.
    /// </remarks>
    private void SampleAndReport()
    {
        _componentMemoryTracker?.Tick();
        _leakDetector?.Tick();

        var now = Stopwatch.GetTimestamp();
        if (_hasReported && now - _lastReportTimestamp < ReportIntervalTimestampTicks)
        {
            return;
        }

        if (!_pendingReport.IsCompleted)
        {
            return;
        }

        _hasReported = true;
        _lastReportTimestamp = now;

        var report = new PeriodicReport(
            _outputDirectory,
            _link,
            Features,
            RandomSeed,
            _frameBudgetTracker?.Snapshot.ToArray(),
            _componentMemoryTracker?.Snapshot.ToArray(),
            _leakDetector?.Findings.ToArray(),
            _gaugeTracker?.TakeIntervalSummaries().ToArray());

        _pendingReport = Task.Run(() => WritePeriodicReport(report));
    }

    private static void WritePeriodicReport(PeriodicReport report)
    {
        try
        {
            ReportFrameBudgetToConsole(report.FrameBudgetSnapshot);
            if (report.Features != DiagnosticsFeatures.None)
            {
                var reportJson = DiagnosticsReportWriter.Write(report.OutputDirectory, report.Features, report.RandomSeed, report.FrameBudgetSnapshot, report.ComponentMemorySnapshot, report.LeakFindings, report.GaugeSummaries);
                report.Link?.SendPeriodicReport(reportJson);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[Diagnostics] Couldn't write the periodic report: {exception.Message}");
        }
    }

    /// <summary>Dumps the full last-second frame-cost ranking to the console -- the whole ranking, not just the top contributor a live readout shows, kept for after a session ends.</summary>
    private static void ReportFrameBudgetToConsole(IReadOnlyList<FrameCostEntry>? snapshot)
    {
        if (snapshot is not { Count: > 0 })
        {
            return;
        }

        Console.WriteLine("[PerformanceProfile] Top costs (ms spent in the last second):");
        foreach (var entry in snapshot)
        {
            Console.WriteLine($"[PerformanceProfile]   [{entry.Category}] {entry.GroupName}.{entry.ItemName}: {entry.MillisecondsPerSecond:N1}ms");
        }
    }

    /// <summary>One periodic report's snapshots, copied so the trackers can keep sampling while it is written.</summary>
    private sealed record PeriodicReport(
        string OutputDirectory,
        DiagnosticsLink? Link,
        DiagnosticsFeatures Features,
        int? RandomSeed,
        FrameCostEntry[]? FrameBudgetSnapshot,
        ComponentMemoryEntry[]? ComponentMemorySnapshot,
        LeakFinding[]? LeakFindings,
        GaugeIntervalSummary[]? GaugeSummaries);
}
