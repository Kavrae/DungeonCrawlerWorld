using Game.Modules.AbilityScores;
using Game.Modules.Health;
using Game.Modules.Health.Components;

namespace Game.Modules.Actions.Effects;

/// <summary>
/// Heals the target -- shared by actions/spells and consumables alike, mirroring DirectDamage's
/// own shape and order of operations:
/// 1. Base amount: FlatAmount + PercentOfMaxHealth * the target's own modifier-effective max
///    health (HealthQueries.TryGetEffectiveMaximum); no-op if &lt;= 0.
/// 2. Add the caster's ability-score tag bonus (AbilityScoreTagBonus -- e.g. a Tag.Wisdom-tagged
///    heal spell would add the caster's own Wisdom total). No built-in heal is tagged with an
///    ability score today, so this is currently always a 0 bonus -- kept for the same future
///    symmetry DirectDamage already has, not because anything uses it yet.
/// 3. HealthHeal.Apply/HealthHeal.ComputeAmount own the rest of the chain -- OutgoingHealing
///    (context.SourceEntityId's own modifiers) then IncomingHealing (context.TargetEntityId's own
///    modifiers), both tag-conditional via context.ActivatorTags (StatModifierComponent.ConditionTag)
///    the same way DirectDamage's OutgoingDamage/HealthDamage's IncomingDamage already are. Kept
///    there rather than inlined here (unlike OutgoingDamage, which DirectDamage applies itself) so
///    SimpleHealthRegenSystem/ComplexHealthRegenSystem's own self-heal tick -- which never goes
///    through DirectHeal at all -- still gets the identical modifier chain.
/// The already-ability-score-adjusted total is passed to HealthHeal.Apply as a flat amount
/// (percentOfMaxHealth: 0) rather than re-passing PercentOfMaxHealth down -- this keeps
/// BodyPartTargetMode.All's "compute the total once, then split evenly" behavior correct: the
/// percent is still resolved against the same overall effective max exactly once, before any
/// per-part split happens.
/// BodyPartTargetMode defaults to All (every body part heals, the total split evenly across them --
/// see ComplexHealthHeal's own doc comment for why) so every existing potion/scroll keeps its
/// "heals everyone" behavior unless it opts into SingleTarget/LowestPercentage.
/// </summary>
public sealed record DirectHeal(
    float PercentOfMaxHealth,
    float FlatAmount = 0f,
    BodyPartType? TargetBodyPartType = null,
    BodyPartTargetMode BodyPartTargetMode = BodyPartTargetMode.All) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        var percentageBaseHeal = 0f;
        if (PercentOfMaxHealth > 0 && HealthQueries.TryGetEffectiveMaximum(context.Health, context.BodyParts, context.StatModifiers, context.TargetEntityId, out var effectiveMaxHealth))
        {
            percentageBaseHeal = PercentOfMaxHealth * effectiveMaxHealth;
        }

        var baseHeal = FlatAmount + percentageBaseHeal;
        if (baseHeal <= 0)
        {
            return;
        }

        var healWithAbilityScoreScaling = baseHeal + AbilityScoreTagBonus.Compute(context.SourceEntityId, context.ActivatorTags, context.AbilityScores);

        BodyPartTargetRule? targetRule = TargetBodyPartType is { } type ? new BodyPartTargetRule(type, BodyPartFallback.Random) : null;
        HealthHeal.Apply(context.Health, context.TargetEntityId, percentOfMaxHealth: 0f, context.StatModifiers, context.BodyParts, flatAmount: healWithAbilityScoreScaling, context.SourceEntityId, context.ActivatorTags, BodyPartTargetMode, targetRule, context.MathUtility, eventBus: context.EventBus, playerQuery: context.PlayerQuery, healType: context.ActivatorName);
    }
}
