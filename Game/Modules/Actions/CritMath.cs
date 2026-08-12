namespace Game.Modules.Actions;

/// <summary>
/// Global crit fallback used by DamageEffectEntry when no StatModifierComponent targets
/// CritChance/CritMultiplier for the caster (mirrors PotionCooldownEffects' constant-holding
/// style). Design intent, stated explicitly rather than left implicit in a bare number: crits
/// here should be rarer but hit harder than the ~15-25%-chance/~1.5-2x-multiplier norm common in
/// games like Diablo/Path of Exile -- a low base chance paired with a noticeably larger base
/// multiplier. Exact tuning is a balance pass, not an architecture decision; these constants
/// record the intent so a future tuning pass doesn't accidentally regress toward the
/// generic-RPG norm.
/// </summary>
public static class CritMath
{
    public const float BaseCritChance = 0.05f;
    public const float BaseCritMultiplier = 3f;
}
