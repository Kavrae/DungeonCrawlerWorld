namespace Workshop.Debug;

/// <summary>A debug run, from the shell's side: the launch options Play starts the game with, and the diagnostics link the game reports back over.</summary>
/// <remarks>Owned by the shell, which launches with LaunchArguments; the Debug tab sets Options (saved at once) and shows the link's session.</remarks>
public sealed class WorkshopDebugging : IDisposable
{
    private readonly DebugLaunchOptionsStore _store;
    private readonly GameLauncher _gameLauncher;
    private DebugLaunchOptions _options;

    /// <param name="reportDirectories">The folders the Reports section lists.</param>
    public WorkshopDebugging(DiagnosticsLinkListener link, DebugLaunchOptionsStore store, GameLauncher gameLauncher, IReadOnlyList<string>? reportDirectories = null)
    {
        Link = link;
        ReportDirectories = reportDirectories ?? [];
        _store = store;
        _gameLauncher = gameLauncher;
        _options = store.Load();
    }

    public DiagnosticsLinkListener Link { get; }

    /// <summary>The folders the Reports section lists diagnostics reports and saved benchmark runs from.</summary>
    public IReadOnlyList<string> ReportDirectories { get; }

    /// <summary>The launch options; setting them saves them for the next Workshop run.</summary>
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
            _store.Save(value);
        }
    }

    /// <summary>Where the options are kept.</summary>
    public string OptionsFilePath => _store.FilePath;

    /// <summary>What Play adds to the game's command line: where the link listens, then the options' arguments.</summary>
    public string LaunchArguments => $"{Link.LaunchArgument} {_options.Arguments}";

    /// <summary>The whole command line Play starts the game with.</summary>
    public string CommandLine => _gameLauncher.CommandLineFor(LaunchArguments);

    /// <summary>Raised when Play or Build and Play started a launch -- the game, or its build first.</summary>
    public event Action? GameLaunched;

    /// <summary>The shell started a launch with LaunchArguments.</summary>
    public void NotifyGameLaunched() => GameLaunched?.Invoke();

    public void Dispose() => Link.Dispose();
}
