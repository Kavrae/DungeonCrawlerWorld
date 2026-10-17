namespace Engine.Diagnostics;

/// <summary>Where the diagnostics engine writes its output files.</summary>
/// <remarks>
/// Walks up from the running assembly's directory to the nearest ancestor containing a .sln
/// file (the repo root, not wherever the build output happens to sit) and appends
/// Log/diagnostics -- matching where this codebase's other file-based diagnostics
/// (PlayerActivityLog's Log/player-activity.log, the phase-performance-testing skill's
/// Log/phase-benchmarks/) already write. Searches for any .sln rather than a hardcoded name, so
/// this stays generic (no knowledge of which specific solution/game is hosting Engine). Falls
/// back to the running directory itself if no .sln is found (e.g. a published, non-repo
/// deployment). Computed once per process -- this doesn't change while running.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
internal static class DiagnosticsPaths
{
    public static string OutputDirectory { get; } = ComputeOutputDirectory();

    private static string ComputeOutputDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && directory.GetFiles("*.sln").Length == 0)
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? AppContext.BaseDirectory;
        return Path.Combine(root, "Log", "diagnostics");
    }
}
