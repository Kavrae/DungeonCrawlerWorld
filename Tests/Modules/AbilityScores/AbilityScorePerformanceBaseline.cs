namespace Tests.Modules.AbilityScores;

/// <summary>
/// Last recorded elapsed time for each AbilityScorePerformanceTests benchmark, on the machine/
/// date noted per constant -- update by hand (with a fresh note) whenever an intentional
/// perf-affecting change lands. See AbilityScorePerformanceTests' own doc comment for why this
/// exists as a new, isolated pattern rather than something read/written automatically.
/// </summary>
internal static class AbilityScorePerformanceBaseline
{
    /// <summary>AbilityScoreEffects.GrantDefaults across 100,000 entities. Recorded 2026-08-06, dev machine, Debug build.</summary>
    public const double GrantDefaultsMilliseconds = 120;

    /// <summary>StatModifierExpirySystem.Update ticking 100,000 temporary ability-score modifiers to expiry (1 frame each). Recorded 2026-08-06, dev machine, Debug build.</summary>
    public const double ExpiryRecomputeMilliseconds = 55;

    /// <summary>Generous multiplier over the recorded baseline before a benchmark is treated as a regression -- absorbs dev/CI machine variance, not meant to catch anything short of a real algorithmic regression (e.g. an accidental O(n^2), or the recompute path getting wired back to run per-frame).</summary>
    public const double ToleranceMultiplier = 1.5;
}
