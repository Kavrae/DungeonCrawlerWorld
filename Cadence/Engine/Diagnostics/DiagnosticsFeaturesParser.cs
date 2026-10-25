namespace Engine.Diagnostics;

/// <summary>Parses a "--diagnostics=frame,memory,startup,leak,gauges" (or "all"/"none") command-line argument into DiagnosticsFeatures.</summary>
/// <remarks>No matching argument, or an explicit "none", both mean None -- diagnostics stay opt-in by default. Unrecognized tokens are ignored rather than throwing, so a typo just leaves that one feature off instead of crashing the game at startup.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class DiagnosticsFeaturesParser
{
    private const string ArgumentPrefix = "--diagnostics=";

    public static DiagnosticsFeatures Parse(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(ArgumentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return ParseValue(arg[ArgumentPrefix.Length..]);
            }
        }

        return DiagnosticsFeatures.None;
    }

    /// <summary>The argument Parse reads back as features: "all" when every feature is on, "none" when none is, else each feature's token.</summary>
    public static string FormatArgument(DiagnosticsFeatures features)
    {
        if ((features & DiagnosticsFeatures.All) == DiagnosticsFeatures.All)
        {
            return $"{ArgumentPrefix}all";
        }

        var tokens = new List<string>();
        AddTokenIfSet(tokens, features, DiagnosticsFeatures.FrameBudget, "frame");
        AddTokenIfSet(tokens, features, DiagnosticsFeatures.Memory, "memory");
        AddTokenIfSet(tokens, features, DiagnosticsFeatures.Startup, "startup");
        AddTokenIfSet(tokens, features, DiagnosticsFeatures.LeakDetection, "leak");
        AddTokenIfSet(tokens, features, DiagnosticsFeatures.Gauges, "gauges");
        return $"{ArgumentPrefix}{(tokens.Count == 0 ? "none" : string.Join(',', tokens))}";
    }

    private static void AddTokenIfSet(List<string> tokens, DiagnosticsFeatures features, DiagnosticsFeatures feature, string token)
    {
        if (features.HasFlag(feature))
        {
            tokens.Add(token);
        }
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
                "gauges" => DiagnosticsFeatures.Gauges,
                _ => DiagnosticsFeatures.None,
            };
        }

        return features;
    }
}
