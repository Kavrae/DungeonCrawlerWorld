using Engine.ECS.Components.Stores;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Tags;

namespace Game.Modules.Actions.Activators;

/// <summary>How a spell scales with its caster's Intelligence: its maximum range, its area and the durations its effects carry, whether it is cast or read from a scroll.</summary>
/// <remarks>
/// A use is a spell's when its activator implies GameTags.Spell -- a SpellActivator, and a ScrollActivator, which casts
/// one -- so a cast, a scroll, a tome's spell and a mastered one scale by the one rule, and a wand, a potion or an
/// ability never does. Costs (activation effects) and the minimum range are not scaled.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class SpellScalingEffects
{
    private const float MinMultiplier = 1.0f;
    private const float MaxMultiplier = 4.0f;

    /// <summary>Whether a use through activator is a spell's, and so scales with its caster's Intelligence; false with no activator.</summary>
    public static bool Applies(IActionActivator? activator) => activator is not null && activator.ImpliedTags.Has(GameTags.Spell);

    /// <summary>The multiplier a spell cast by entityId scales by: its Intelligence total's, or 1 with no Intelligence score.</summary>
    public static float MultiplierFor(PackedComponentPool<AbilityScoresComponent> abilityScores, int entityId) =>
        AbilityScoreQueries.TryGetComponent(abilityScores, entityId, AbilityScoreType.Intelligence, out var intelligence)
            ? ComputeScaleMultiplier(intelligence.Total)
            : MinMultiplier;

    /// <summary>The multiplier entityId's use through activator scales by: MultiplierFor for a spell's, 1 for anything else.</summary>
    public static float MultiplierFor(PackedComponentPool<AbilityScoresComponent> abilityScores, int entityId, IActionActivator? activator) =>
        Applies(activator) ? MultiplierFor(abilityScores, entityId) : MinMultiplier;

    public static float ComputeScaleMultiplier(ushort intelligenceTotal) =>
        AbilityScoreMath.Lerp(intelligenceTotal, MinMultiplier, MaxMultiplier);

    public static TargetingSpec ScaleTargeting(TargetingSpec baseTargeting, float multiplier) =>
        baseTargeting with
        {
            MaximumRange = (int)Math.Round(baseTargeting.MaximumRange * multiplier),
            AreaSize = (int)Math.Round(baseTargeting.AreaSize * multiplier),
        };
}
