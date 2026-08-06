using Engine.ECS.Components;
using Game.Modules.AbilityScores.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.AbilityScores;

/// <summary>
/// Write surface for ability scores -- mirrors StatModifierEffects' static style, fetching its
/// own pools from ComponentManager rather than taking them as parameters. Grant/GrantDefaults
/// set up an entity's starting scores (blueprint-build time); GrantModifier is what any future
/// consumer (equipment, level-up, buffs -- see TODO.md) must call instead of raw
/// StatModifierEffects.Apply when the target is an ability score, so Total stays precomputed
/// (see AbilityScoreComponent's own doc comment for why this is eager, not StatModifierMath's
/// lazy-on-read convention). The other half of that guarantee -- keeping Total in sync when a
/// temporary ability-score modifier expires -- lives in AbilityScoresModule's
/// StatModifierExpiredEvent subscription, which goes through RecomputeIfAbilityScore below so
/// the actual recompute logic (RecomputeAbilityScore) exists exactly once.
/// </summary>
public static class AbilityScoreEffects
{
    public static void Grant(ComponentManager componentManager, int entityId, AbilityScoreType type, short baseValue)
    {
        var clampedBase = AbilityScoreMath.ClampBaseValue(baseValue);
        var statModifiers = componentManager.IsRegistered<StatModifierComponent>()
            ? componentManager.GetMultiPool<StatModifierComponent>()
            : null;
        var total = AbilityScoreMath.ComputeTotal(statModifiers, entityId, type, clampedBase);

        componentManager.GetMultiPool<AbilityScoreComponent>().Add(entityId, new AbilityScoreComponent(type, clampedBase, total));
    }

    public static void GrantDefaults(ComponentManager componentManager, int entityId, short baseValue)
    {
        foreach (var type in Enum.GetValues<AbilityScoreType>())
        {
            Grant(componentManager, entityId, type, baseValue);
        }
    }

    /// <summary>
    /// Entry point for any future code (equipment, level-up, buffs -- see TODO.md) granting a
    /// modifier that targets an ability score. Takes AbilityScoreType rather than
    /// StatModifierTarget on purpose: a modifier that doesn't target an ability score has no
    /// business going through this class at all -- callers granting e.g. an IncomingDamage
    /// modifier should call StatModifierEffects.Apply directly, the same as every other
    /// existing call site does. Restricting the parameter this way makes it impossible to call
    /// this method for a target RecomputeAbilityScore couldn't do anything with.
    /// </summary>
    public static void GrantModifier(
        ComponentManager componentManager,
        int entityId,
        AbilityScoreType type,
        StatModifierOperation operation,
        StatModifierPolarity polarity,
        bool canModify,
        float magnitude,
        int durationFrames,
        StatusEffectSource source)
    {
        StatModifierEffects.Apply(componentManager, entityId, AbilityScoreMath.ToStatModifierTarget(type), operation, polarity, canModify, magnitude, durationFrames, source);
        RecomputeAbilityScore(componentManager, entityId, type);
    }

    /// <summary>
    /// Maps target to an AbilityScoreType and recomputes if it is one, a no-op otherwise --
    /// the bridge AbilityScoresModule's StatModifierExpiredEvent subscription needs, since that
    /// event is generic (published for every expired modifier, not just ability-score ones) and
    /// has no equivalent of GrantModifier's compile-time restriction to lean on.
    /// </summary>
    public static void RecomputeIfAbilityScore(ComponentManager componentManager, int entityId, StatModifierTarget target)
    {
        var type = AbilityScoreMath.FromStatModifierTarget(target);
        if (type is not null)
        {
            RecomputeAbilityScore(componentManager, entityId, type.Value);
        }
    }

    /// <summary>
    /// Recomputes and stores Total for the one AbilityScoreComponent instance matching type, if
    /// the entity has one -- shared by GrantModifier (called inline, right after adding the
    /// modifier, with a type it already knows) and by RecomputeIfAbilityScore above (called once
    /// StatModifiersModule's expiry event resolves to a type). No-ops if AbilityScoresModule
    /// isn't registered at all (e.g. a StatModifiers-only test).
    /// </summary>
    private static void RecomputeAbilityScore(ComponentManager componentManager, int entityId, AbilityScoreType type)
    {
        if (!componentManager.IsRegistered<AbilityScoreComponent>())
        {
            return;
        }

        var abilityScores = componentManager.GetMultiPool<AbilityScoreComponent>();
        var statModifiers = componentManager.IsRegistered<StatModifierComponent>()
            ? componentManager.GetMultiPool<StatModifierComponent>()
            : null;

        for (var denseIndex = abilityScores.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = abilityScores.GetNextDenseIndex(denseIndex))
        {
            ref readonly var abilityScore = ref abilityScores.GetReadonlyByDenseIndex(denseIndex);
            if (abilityScore.Type != type)
            {
                continue;
            }

            var newTotal = AbilityScoreMath.ComputeTotal(statModifiers, entityId, type, abilityScore.BaseValue);
            abilityScores.UpdateByDenseIndex(denseIndex, newTotal, static (ref AbilityScoreComponent component, short total) => component.Total = total);
            return;
        }
    }
}
