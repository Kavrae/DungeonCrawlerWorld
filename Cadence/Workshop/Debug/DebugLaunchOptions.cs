using Engine.Diagnostics;

namespace Workshop.Debug;

/// <summary>How Play starts a debug run: the diagnostics features the game runs with.</summary>
/// <remarks>The other launch options -- seed, map size, benchmark range, headless, module settings -- are TODO.md "Workshop Debug tab: launch options".</remarks>
public sealed record DebugLaunchOptions(DiagnosticsFeatures DiagnosticsFeatures)
{
    /// <summary>Frame budget and gauges: what the Live view shows most of.</summary>
    public static readonly DebugLaunchOptions Default = new(DiagnosticsFeatures.FrameBudget | DiagnosticsFeatures.Gauges);

    /// <summary>The game's arguments these options make.</summary>
    public string Arguments => DiagnosticsFeaturesParser.FormatArgument(DiagnosticsFeatures);
}
