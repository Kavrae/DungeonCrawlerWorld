using Engine.Diagnostics;

namespace Workshop.Debug;

/// <summary>A debug run, from the shell's side: the launch options Play starts the game with, the diagnostics link the game reports back over, and what the run launched last is doing.</summary>
/// <remarks>
/// Owned by the shell, which launches with LaunchArguments, hands it the game's output and updates it once per frame; the
/// Debug tab sets Options (saved once they have stopped changing for SaveDelayFrames updates, at a launch, or on Dispose)
/// and shows the link's session. Each launch keeps the options it was started with
/// (LaunchedOptions), so what is said about the running game holds after the options are edited. Update writes the status
/// line whenever what it says changes: the launcher's status, the connection's, a completed benchmark -- and, for a
/// headless run, which has no window to watch, its progress toward the range's end and how it ended.
/// </remarks>
public sealed class WorkshopDebugging : IDisposable
{
    /// <summary>How many updates Options must go unchanged before they are saved, so a field scrolled or typed into writes the file once.</summary>
    public const int SaveDelayFrames = 30;

    private readonly DebugLaunchOptionsStore _store;
    private readonly GameLauncher _gameLauncher;
    private DebugLaunchOptions _options;
    private int _updatesUntilSave;
    private LiveDiagnosticsSession? _sessionAtLaunch;
    private RunStatusKey _shownStatus;

    /// <param name="reportDirectories">The folders the Reports section lists.</param>
    public WorkshopDebugging(DiagnosticsLinkListener link, DebugLaunchOptionsStore store, GameLauncher gameLauncher, IReadOnlyList<string>? reportDirectories = null)
    {
        Link = link;
        ReportDirectories = reportDirectories ?? [];
        _store = store;
        _gameLauncher = gameLauncher;
        _options = store.Load();
        Problems = _options.ProblemsWith(gameLauncher.TargetArguments);
    }

    public DiagnosticsLinkListener Link { get; }

    /// <summary>The folders the Reports section lists diagnostics reports and saved benchmark runs from.</summary>
    public IReadOnlyList<string> ReportDirectories { get; }

    /// <summary>The launch options; setting them saves them for the next Workshop run, once they stop changing.</summary>
    public DebugLaunchOptions Options
    {
        get => _options;
        set
        {
            if (value == _options)
            {
                return;
            }

            _options = value;
            OptionsVersion++;
            Problems = _options.ProblemsWith(_gameLauncher.TargetArguments);
            _updatesUntilSave = SaveDelayFrames;
        }
    }

    /// <summary>Changes every time Options does.</summary>
    public int OptionsVersion { get; private set; }

    /// <summary>What the game would refuse or read differently than Options say, the launch target's own arguments included.</summary>
    public IReadOnlyList<DebugLaunchProblem> Problems { get; private set; }

    /// <summary>Why Play would start nothing with these options, or null when it can launch.</summary>
    public string? LaunchRefusal => Problems.FirstOrDefault(static problem => problem.RefusesLaunch)?.Message;
    /// <summary>Where the options are kept.</summary>
    public string OptionsFilePath => _store.FilePath;

    /// <summary>What Play adds to the game's command line: where the link listens, then the options' arguments.</summary>
    public string LaunchArguments => $"{Link.LaunchArgument} {_options.Arguments}";

    /// <summary>The whole command line Play starts the game with.</summary>
    public string CommandLine => _gameLauncher.CommandLineFor(LaunchArguments);

    /// <summary>The options the last launch started with; null before any.</summary>
    public DebugLaunchOptions? LaunchedOptions { get; private set; }

    /// <summary>The whole command line the last launch started with; empty before any.</summary>
    public string LaunchedCommandLine { get; private set; } = string.Empty;

    /// <summary>Whether the last launch is still building or its game still running.</summary>
    public bool IsGameRunning => _gameLauncher.IsGameRunning;

    /// <summary>The session of the game the last launch started, once it has connected; null before.</summary>
    public LiveDiagnosticsSession? LaunchedSession => LaunchedOptions is not null && Link.Session is { } session && !ReferenceEquals(session, _sessionAtLaunch) ? session : null;

    /// <summary>The files the latest benchmark any connected game completed wrote; null before one.</summary>
    public DiagnosticsLinkBenchmarkCompletion? CompletedBenchmark { get; private set; }

    /// <summary>Changes every time CompletedBenchmark does.</summary>
    public int CompletedBenchmarkVersion { get; private set; }

    /// <summary>The fingerprint the last launch's headless run wrote as it finished; null until it does.</summary>
    public string? Fingerprint { get; private set; }

    /// <summary>Whether the Debug tab is open on its Reports section, where a completed benchmark opens, so the status line needn't point there. Set by the tab.</summary>
    public bool ShowsReports { get; set; }

    /// <summary>Raised when Play or Build and Play started a launch -- the game, or its build first.</summary>
    public event Action? GameLaunched;

    /// <summary>The shell started a launch with LaunchArguments.</summary>
    public void NotifyGameLaunched()
    {
        SaveOptionsIfChanged();
        LaunchedOptions = _options;
        LaunchedCommandLine = CommandLine;
        _sessionAtLaunch = Link.Session;
        Fingerprint = null;
        GameLaunched?.Invoke();
    }

    /// <summary>Ends the build or game the last launch started; false when nothing is running.</summary>
    /// <param name="status">What happened, for the status line.</param>
    public bool Stop(out string status) => _gameLauncher.Stop(out status);

    /// <summary>Reads one line of the game's captured output: a headless run's fingerprint line sets Fingerprint.</summary>
    public void ObserveGameOutput(GameOutputLine line)
    {
        if (!line.IsError && HeadlessArgument.TryParseFingerprintLine(line.Text, out var fingerprint))
        {
            Fingerprint = fingerprint;
        }
    }

    /// <summary>Saves Options once they have gone SaveDelayFrames updates unchanged, takes up a newly completed benchmark, and writes the status line if what it says has changed. UI thread, once per frame, after the link's Update and the game's output.</summary>
    public void Update(IWorkshopStatus status)
    {
        if (_updatesUntilSave > 0 && --_updatesUntilSave == 0)
        {
            _store.Save(_options);
        }

        var session = Link.Session;
        if (session?.LatestBenchmarkCompletion is { } completion && !ReferenceEquals(completion, CompletedBenchmark))
        {
            CompletedBenchmark = completion;
            CompletedBenchmarkVersion++;
        }

        var launchedSession = LaunchedSession;
        var progressBucket = LaunchedOptions is { RunsHeadless: true } && launchedSession is not null ? launchedSession.LatestFrame / DebugTabChrome.TextRefreshFrames : 0;
        var key = new RunStatusKey(_gameLauncher.StatusVersion, session, session?.StatusVersion ?? 0, CompletedBenchmarkVersion, Fingerprint, progressBucket);
        if (key == _shownStatus)
        {
            return;
        }

        var previous = _shownStatus;
        _shownStatus = key;
        string? message = null;
        if (key.LauncherStatusVersion != previous.LauncherStatusVersion)
        {
            message = _gameLauncher.Status;
        }

        if (session is not null && (!ReferenceEquals(session, previous.Session) || key.SessionStatusVersion != previous.SessionStatusVersion))
        {
            message = session.StatusText;
        }

        if (CompletedBenchmark is { } completed && (key.CompletedBenchmarkVersion != previous.CompletedBenchmarkVersion || (key.Fingerprint is not null && key.Fingerprint != previous.Fingerprint)))
        {
            message = CompletionText(completed);
        }

        if (LaunchedOptions is { RunsHeadless: true } launched)
        {
            message = HeadlessStatus(launched, launchedSession);
        }

        if (message is not null)
        {
            status.Show(message);
        }
    }

    /// <summary>"Frame N of END" for a run measuring range, as of frame; at most END.</summary>
    public static string ProgressText(long frame, BenchmarkFrameRange range) =>
        $"frame {System.Math.Min(frame, range.EndFrame):N0} of {range.EndFrame:N0}";

    public void Dispose()
    {
        SaveOptionsIfChanged();
        Link.Dispose();
    }

    private void SaveOptionsIfChanged()
    {
        if (_updatesUntilSave == 0)
        {
            return;
        }

        _updatesUntilSave = 0;
        _store.Save(_options);
    }

    /// <summary>What a headless run is doing: stopped, complete, ended early, or how far it has got.</summary>
    private string HeadlessStatus(DebugLaunchOptions launched, LiveDiagnosticsSession? session)
    {
        var exit = _gameLauncher.LastExit;
        if (exit is { WasStopped: true })
        {
            return GameLauncher.GameStoppedStatus;
        }

        if (session?.LatestBenchmarkCompletion is { } completion)
        {
            return CompletionText(completion);
        }

        if (!_gameLauncher.IsGameRunning)
        {
            var linkClosed = session is null || session.State == DiagnosticsConnectionState.Ended;
            return exit is not null && linkClosed ? EndedEarlyText(exit) : _gameLauncher.Status;
        }

        return session?.History is not null
            ? $"Headless benchmark: {ProgressText(session.LatestFrame, launched.BenchmarkFrames)}"
            : _gameLauncher.Status;
    }

    private string CompletionText(DiagnosticsLinkBenchmarkCompletion completion)
    {
        var fingerprint = Fingerprint is { } value ? $", fingerprint {value}" : string.Empty;
        var pointer = ShowsReports ? "." : " -- open Debug > Reports";
        return $"Benchmark complete: {Path.GetFileName(completion.BenchmarkReportPath)}{fingerprint}{pointer}";
    }

    /// <summary>A headless run whose game exited with no completed benchmark: the exit code and, when the game refused its arguments (exit code 2), its own error line.</summary>
    private string EndedEarlyText(GameExit exit)
    {
        if (exit.ExitCode == 0)
        {
            var fingerprint = Fingerprint is { } value ? $" (fingerprint {value})" : string.Empty;
            return $"Headless benchmark finished{fingerprint}, but the diagnostics link sent no completed benchmark.";
        }

        return exit.ExitCode == 2 && exit.LastErrorLine is { } errorLine
            ? $"Headless benchmark ended early (exit code 2): {errorLine}"
            : $"Headless benchmark ended early (exit code {exit.ExitCode}).";
    }

    /// <summary>Everything the status line's text is made from; a change to any of it is something to say.</summary>
    private readonly record struct RunStatusKey(int LauncherStatusVersion, LiveDiagnosticsSession? Session, int SessionStatusVersion, int CompletedBenchmarkVersion, string? Fingerprint, long ProgressBucket);
}
