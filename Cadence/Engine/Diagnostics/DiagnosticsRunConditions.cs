namespace Engine.Diagnostics;

/// <summary>What a run's measurements were taken under, written on its benchmark report so two reports are compared only when they ran alike.</summary>
/// <remarks>
/// Each of these changes what the same code costs: the diagnostics features on decide how much recording every measured
/// call does (FrameBudget adds a second recorder; Memory holds a copy of every pool across the range), a headless run has
/// no Draw between frames, a link adds a sender thread, and a Debug build runs unoptimised. DifferencesFrom names each
/// way two runs differ, for a comparison to show before its numbers.
/// </remarks>
/// <param name="Features">The DiagnosticsFeatures that were on, by name.</param>
/// <param name="IsHeadless">Whether the simulation ran with no window, back to back.</param>
/// <param name="HasLink">Whether an IDE link was streaming the run.</param>
/// <param name="BuildConfiguration">"Debug" or "Release": how the engine, and the game built with it, were compiled.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsRunConditions(string Features, bool IsHeadless, bool HasLink, string BuildConfiguration)
{
    /// <summary>How this engine assembly was compiled.</summary>
    public const string CurrentBuildConfiguration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    /// <summary>Each way other's conditions differ from these, as a short phrase; empty when they ran alike.</summary>
    public IReadOnlyList<string> DifferencesFrom(DiagnosticsRunConditions other)
    {
        var differences = new List<string>();
        if (BuildConfiguration != other.BuildConfiguration)
        {
            differences.Add($"build {BuildConfiguration} vs {other.BuildConfiguration}");
        }

        if (IsHeadless != other.IsHeadless)
        {
            differences.Add($"{HostName(IsHeadless)} vs {HostName(other.IsHeadless)}");
        }

        if (Features != other.Features)
        {
            differences.Add($"features {Features} vs {other.Features}");
        }

        if (HasLink != other.HasLink)
        {
            differences.Add($"{LinkName(HasLink)} vs {LinkName(other.HasLink)}");
        }

        return differences;
    }

    private static string HostName(bool isHeadless) => isHeadless ? "headless" : "windowed";

    private static string LinkName(bool hasLink) => hasLink ? "linked" : "unlinked";
}
