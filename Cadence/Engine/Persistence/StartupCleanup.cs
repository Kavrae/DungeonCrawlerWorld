using Engine.Diagnostics;

namespace Engine.Persistence;

/// <summary>What StartupCleanup did: the log files it deleted, how many save definition lines it dropped, and what it couldn't do.</summary>
public sealed record StartupCleanupResult(IReadOnlyList<string> DeletedLogFiles, int DefinitionLinesCompacted, IReadOnlyList<string> Problems)
{
    /// <summary>One line for each thing done, then one per problem, each beginning "[Startup]"; none when there was nothing to do.</summary>
    public IEnumerable<string> ReportLines()
    {
        if (DeletedLogFiles.Count > 0)
        {
            yield return $"[Startup] Deleted {DeletedLogFiles.Count} expired log files";
        }

        if (DefinitionLinesCompacted > 0)
        {
            yield return $"[Startup] Compacted {DefinitionLinesCompacted} superseded or removed save definition lines";
        }

        foreach (var problem in Problems)
        {
            yield return $"[Startup] {problem}";
        }
    }
}

/// <summary>The housekeeping a host does once as it starts, before it builds anything: deletes its logs' dated files past the retention, and compacts the save it is about to play.</summary>
/// <remarks>
/// Run while nothing reads or writes the files it touches: before a RollingLogFile over the same logs is opened, and
/// before a session reads the save's Definitions layer (a session's index holds where each line is). Something it can't
/// delete or rewrite is left as it is and named in the result, never stopping the start.
/// </remarks>
public static class StartupCleanup
{
    /// <param name="logs">Each log whose dated files past its retention are deleted (RollingLogFile.DeleteExpiredFiles).</param>
    /// <param name="save">The save the host is about to play, compacted (SaveFolder.CompactDefinitions); null for none.</param>
    /// <param name="time">Where today's date comes from; the system clock when null.</param>
    public static StartupCleanupResult Run(IEnumerable<RollingLogTarget> logs, SaveFolder? save, TimeProvider? time = null)
    {
        var problems = new List<string>();
        var deletedLogFiles = new List<string>();
        foreach (var log in logs)
        {
            deletedLogFiles.AddRange(RollingLogFile.DeleteExpiredFiles(log, problems, time));
        }

        var compacted = save?.CompactDefinitions(problems) ?? 0;
        return new StartupCleanupResult(deletedLogFiles, compacted, problems);
    }
}
