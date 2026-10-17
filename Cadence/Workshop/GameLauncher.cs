using System.Diagnostics;

namespace Workshop;

/// <summary>Where Play finds the game: its executable, the arguments it starts with, and the folder it starts in.</summary>
public sealed record GameLaunchTarget(string ExecutablePath, string Arguments, string WorkingDirectory);

/// <summary>Starts the game as a process of its own, so playing never depends on the IDE and a crash in one doesn't take the other.</summary>
/// <remarks>One game at a time: Play while the last game Workshop started is still running starts nothing.</remarks>
public sealed class GameLauncher(GameLaunchTarget target)
{
    /// <summary>What the status line says once the game Play started has exited.</summary>
    public const string GameClosedStatus = "Game closed.";

    private Process? _runningGame;

    /// <summary>Whether the last game this launcher started is still running.</summary>
    public bool IsGameRunning => _runningGame is { HasExited: false };

    /// <summary>Starts the game unless one this launcher started is still running or the executable isn't built; status says which happened.</summary>
    public bool TryLaunch(out string status)
    {
        if (IsGameRunning)
        {
            status = "The game is already running.";
            return false;
        }

        if (!File.Exists(target.ExecutablePath))
        {
            status = $"Game not built: {target.ExecutablePath}";
            return false;
        }

        _runningGame?.Dispose();
        _runningGame = Process.Start(new ProcessStartInfo(target.ExecutablePath, target.Arguments)
        {
            WorkingDirectory = target.WorkingDirectory,
            UseShellExecute = false,
        });

        if (_runningGame is null)
        {
            status = $"The game did not start: {target.ExecutablePath}";
            return false;
        }

        status = "Game started.";
        return true;
    }
}
