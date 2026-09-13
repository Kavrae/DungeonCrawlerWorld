namespace Engine.Diagnostics;

/// <summary>The simulation frames a FrameRangeBenchmark measures: StartFrame inclusive to EndFrame exclusive.</summary>
/// <remarks>
/// Parsed from a "--benchmark-frames=600-3600" command-line argument. Same "--name=value" shape
/// as DiagnosticsFeaturesParser and RandomSeed, and the same tolerance: a missing or malformed
/// value means no benchmark rather than a crash at startup.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct BenchmarkFrameRange
{
    private const string ArgumentPrefix = "--benchmark-frames=";

    public BenchmarkFrameRange(long startFrame, long endFrame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startFrame);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endFrame, startFrame);

        StartFrame = startFrame;
        EndFrame = endFrame;
    }

    /// <summary>First simulation frame measured.</summary>
    public long StartFrame { get; }

    /// <summary>First simulation frame no longer measured.</summary>
    public long EndFrame { get; }

    /// <summary>How many simulation frames the range covers.</summary>
    public long FrameCount => EndFrame - StartFrame;

    /// <summary>Reads the range from args; null when absent or malformed (not "start-end" with 0 &lt;= start &lt; end).</summary>
    public static BenchmarkFrameRange? Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        foreach (var arg in args)
        {
            if (!arg.StartsWith(ArgumentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = arg[ArgumentPrefix.Length..].Split('-');
            if (parts.Length == 2 &&
                long.TryParse(parts[0], out var startFrame) &&
                long.TryParse(parts[1], out var endFrame) &&
                startFrame >= 0 &&
                endFrame > startFrame)
            {
                return new BenchmarkFrameRange(startFrame, endFrame);
            }

            return null;
        }

        return null;
    }
}
