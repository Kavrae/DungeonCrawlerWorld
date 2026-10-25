namespace Engine.Diagnostics;

/// <summary>The "--headless" command-line argument: run the simulation with no window until a benchmark range closes.</summary>
/// <remarks>Headless is benchmark-only: with no range there is nothing to stop it, and no window to close, so a game given it without "--benchmark-frames=" refuses to start.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class HeadlessArgument
{
    public const string Argument = "--headless";

    /// <summary>What a game given Argument without a benchmark range says before refusing to start.</summary>
    public const string RequiresRangeMessage = $"{Argument} requires {BenchmarkFrameRange.ArgumentPrefix}START-END.";

    /// <summary>What starts the line a headless run writes to standard output once its range closes, before the fingerprint of its final world state.</summary>
    public const string FingerprintLinePrefix = "[Headless] Fingerprint ";

    /// <summary>The fingerprint line's text for fingerprint.</summary>
    public static string FormatFingerprintLine(string fingerprint) => FingerprintLinePrefix + fingerprint;

    /// <summary>The fingerprint a line of a headless run's output carries; false for any other line.</summary>
    public static bool TryParseFingerprintLine(string line, out string fingerprint)
    {
        if (line.StartsWith(FingerprintLinePrefix, StringComparison.Ordinal) && line.Length > FingerprintLinePrefix.Length)
        {
            fingerprint = line[FingerprintLinePrefix.Length..].Trim();
            return true;
        }

        fingerprint = string.Empty;
        return false;
    }

    public static bool IsPresent(IEnumerable<string> args) => args.Contains(Argument, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a game given args refuses to start: Argument without a benchmark range BenchmarkFrameRange.Parse accepts.</summary>
    public static bool IsRefused(IReadOnlyList<string> args) => IsPresent(args) && BenchmarkFrameRange.Parse(args) is null;
}
