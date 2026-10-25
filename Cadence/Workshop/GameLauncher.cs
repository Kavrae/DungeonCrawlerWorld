using System.Collections.Concurrent;
using System.Diagnostics;

namespace Workshop;

/// <summary>A program to run: its executable, the arguments it starts with, and the folder it starts in.</summary>
public sealed record ProcessCommand(string ExecutablePath, string Arguments, string WorkingDirectory);

/// <summary>Where Play finds the game: its executable, the arguments it starts with, the folder it starts in, and what builds it, if anything.</summary>
/// <param name="BuildCommand">Run by a launch that asks to build first; the game starts only if it exits with code 0. Null when the game can't be built from here.</param>
public sealed record GameLaunchTarget(string ExecutablePath, string Arguments, string WorkingDirectory, ProcessCommand? BuildCommand = null);

/// <summary>One line the game or its build wrote, and whether it went to standard error.</summary>
public readonly record struct GameOutputLine(string Text, bool IsError);

/// <summary>Starts the game as a process of its own, so playing never depends on the IDE and a crash in one doesn't take the other.</summary>
/// <remarks>
/// One game at a time: a launch while the last one is still building or running starts nothing. The game's and its build's
/// output is captured line by line (both are windowed programs with no console of their own) and handed out by DrainOutput.
/// Status says what happened last, and StatusVersion changes with it, so a shell reads it once per change rather than
/// polling the process. Building and exiting happen on other threads; everything here is safe to read from the UI thread.
/// </remarks>
public sealed class GameLauncher(GameLaunchTarget target) : IDisposable
{
    /// <summary>What the status line says once the game a launch started has exited with code 0.</summary>
    public const string GameClosedStatus = "Game closed.";

    private readonly ConcurrentQueue<GameOutputLine> _pendingOutput = new();
    private readonly Lock _stateLock = new();
    private Process? _runningGame;
    private bool _isBusy;
    private string _status = string.Empty;
    private int _statusVersion;
    private int _errorLineCount;

    /// <summary>Whether a launch is still building or the game it started is still running.</summary>
    public bool IsGameRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _isBusy;
            }
        }
    }

    /// <summary>Whether the target can build the game before starting it.</summary>
    public bool CanBuild => target.BuildCommand is not null;

    /// <summary>What happened last: started, building, closed, or why nothing started.</summary>
    public string Status
    {
        get
        {
            lock (_stateLock)
            {
                return _status;
            }
        }
    }

    /// <summary>Changes every time Status does.</summary>
    public int StatusVersion
    {
        get
        {
            lock (_stateLock)
            {
                return _statusVersion;
            }
        }
    }

    /// <summary>Starts the game, or its build first when buildFirst; false when one is already building or running, or the executable can't be started.</summary>
    /// <param name="status">What happened -- the same text Status now holds.</param>
    /// <param name="extraArguments">Added after the target's own arguments for this launch only, such as a seed or a setting.</param>
    /// <param name="buildFirst">Run the target's BuildCommand and start the game only if it succeeds; ignored when the target has none.</param>
    public bool TryLaunch(out string status, string? extraArguments = null, bool buildFirst = false)
    {
        lock (_stateLock)
        {
            if (_isBusy)
            {
                status = SetStatus("The game is already running.");
                return false;
            }

            if (buildFirst && target.BuildCommand is { } buildCommand)
            {
                _isBusy = true;
                status = SetStatus("Building...");
                _ = Task.Run(() => BuildThenStart(buildCommand, extraArguments));
                return true;
            }

            var started = TryStartGame(extraArguments, out status);
            _isBusy = started;
            return started;
        }
    }

    /// <summary>The command line a launch with extraArguments starts the game with: the executable, quoted, then its arguments.</summary>
    public string CommandLineFor(string? extraArguments)
    {
        var arguments = ArgumentsFor(extraArguments);
        return arguments.Length == 0 ? $"\"{target.ExecutablePath}\"" : $"\"{target.ExecutablePath}\" {arguments}";
    }

    /// <summary>Moves every output line received since the last call into destination, oldest first, and returns how many.</summary>
    public int DrainOutput(List<GameOutputLine> destination)
    {
        var count = 0;
        while (_pendingOutput.TryDequeue(out var line))
        {
            destination.Add(line);
            count++;
        }

        return count;
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            _runningGame?.Dispose();
            _runningGame = null;
        }
    }

    private void BuildThenStart(ProcessCommand buildCommand, string? extraArguments)
    {
        int exitCode;
        try
        {
            using var build = StartCapturing(buildCommand.ExecutablePath, buildCommand.Arguments, buildCommand.WorkingDirectory);
            if (build is null)
            {
                FinishWithoutGame($"The build did not start: {buildCommand.ExecutablePath}");
                return;
            }

            build.WaitForExit();
            exitCode = build.ExitCode;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            FinishWithoutGame($"The build did not start: {exception.Message}");
            return;
        }

        if (exitCode != 0)
        {
            FinishWithoutGame($"Build failed (exit code {exitCode}).");
            return;
        }

        lock (_stateLock)
        {
            _isBusy = TryStartGame(extraArguments, out _);
        }
    }

    private void FinishWithoutGame(string status)
    {
        lock (_stateLock)
        {
            _isBusy = false;
            SetStatus(status);
        }
    }

    /// <summary>Starts the game; called holding _stateLock.</summary>
    private bool TryStartGame(string? extraArguments, out string status)
    {
        if (!File.Exists(target.ExecutablePath))
        {
            status = SetStatus($"Game not built: {target.ExecutablePath}");
            return false;
        }

        _runningGame?.Dispose();
        _errorLineCount = 0;
        try
        {
            _runningGame = StartCapturing(target.ExecutablePath, ArgumentsFor(extraArguments), target.WorkingDirectory, OnGameExited);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _runningGame = null;
            status = SetStatus($"The game did not start: {exception.Message}");
            return false;
        }

        if (_runningGame is null)
        {
            status = SetStatus($"The game did not start: {target.ExecutablePath}");
            return false;
        }

        status = SetStatus("Game started.");
        return true;
    }

    private string ArgumentsFor(string? extraArguments) =>
        string.IsNullOrWhiteSpace(extraArguments) ? target.Arguments : $"{target.Arguments} {extraArguments}".Trim();

    private Process? StartCapturing(string executablePath, string arguments, string workingDirectory, EventHandler? exited = null)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(executablePath, arguments)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
            EnableRaisingEvents = exited is not null,
        };

        process.OutputDataReceived += (_, line) => Capture(line.Data, isError: false);
        process.ErrorDataReceived += (_, line) => Capture(line.Data, isError: true);
        if (exited is not null)
        {
            process.Exited += exited;
        }

        if (!process.Start())
        {
            process.Dispose();
            return null;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private void Capture(string? text, bool isError)
    {
        if (text is null)
        {
            return;
        }

        if (isError)
        {
            Interlocked.Increment(ref _errorLineCount);
        }

        _pendingOutput.Enqueue(new GameOutputLine(text, isError));
    }

    private void OnGameExited(object? sender, EventArgs eventArgs)
    {
        if (sender is not Process game)
        {
            return;
        }

        game.WaitForExit();
        lock (_stateLock)
        {
            if (!ReferenceEquals(game, _runningGame))
            {
                return;
            }

            _isBusy = false;
            var errorLines = Volatile.Read(ref _errorLineCount);
            SetStatus(game.ExitCode == 0 && errorLines == 0
                ? GameClosedStatus
                : $"Game closed (exit code {game.ExitCode}, {errorLines} error line{(errorLines == 1 ? "" : "s")}).");
        }
    }

    /// <summary>Sets Status and moves StatusVersion on; called holding _stateLock.</summary>
    private string SetStatus(string status)
    {
        _status = status;
        _statusVersion++;
        return status;
    }
}
