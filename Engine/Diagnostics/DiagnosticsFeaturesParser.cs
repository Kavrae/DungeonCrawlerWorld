namespace Engine.Diagnostics;

/// <summary>Parses a "--diagnostics=frame,memory,startup,leak" (or "all"/"none") command-line argument into DiagnosticsFeatures.</summary>
/// <remarks>No matching argument, or an explicit "none", both mean None -- diagnostics stay opt-in by default. Unrecognized tokens are ignored rather than throwing, so a typo just leaves that one feature off instead of crashing the game at startup.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class DiagnosticsFeaturesParser
{
    private const string ArgumentPrefix = "--diagnostics=";

    public static DiagnosticsFeatures Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        foreach (var arg in args)
        {
            if (arg.StartsWith(ArgumentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return ParseValue(arg[ArgumentPrefix.Length..]);
            }
        }

        return DiagnosticsFeatures.None;
    }

    private static DiagnosticsFeatures ParseValue(string value)
    {
        var features = DiagnosticsFeatures.None;

        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            features |= token.ToLowerInvariant() switch
            {
                "all" => DiagnosticsFeatures.All,
                "none" => DiagnosticsFeatures.None,
                "frame" or "framebudget" => DiagnosticsFeatures.FrameBudget,
                "memory" => DiagnosticsFeatures.Memory,
                "startup" => DiagnosticsFeatures.Startup,
                "leak" or "leakdetection" => DiagnosticsFeatures.LeakDetection,
                _ => DiagnosticsFeatures.None,
            };
        }

        return features;
    }
}
