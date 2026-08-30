using Game.Modules.AbilityScores;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.World;

namespace Game.Modules.Actions.Effects;

/// <summary>
/// Deals damage -- shared by actions and consumables alike (a thrown explosive deals damage
/// the same way a spell does; nothing here is action-specific). MinFlatDamage/MaxFlatDamage is rolled
/// unless context.DamageOverride is set (ActionInstanceComponent's per-instance, per-race
/// override -- see ActionEffectContext's own doc comment; consumables never set it, so they
/// always roll the range); PercentageDamage (if set) adds a further percentage of the target's
/// own modifier-effective max health on top (HealthQueries.TryGetEffectiveMaximum), converted to
/// a flat number and added before anything else runs -- an execute-style effect ("+10% of target's
/// max health") doesn't need any special-casing further down the chain. Order of operations,
/// itself an application of the "composition order is meaningful" rule one level down:
/// 1. Base amount: (DamageOverride if set, else a MinFlatDamage..MaxFlatDamage roll) + PercentageDamage * target's effective max health.
/// 2. Add the caster's ability-score tag bonus (AbilityScoreTagBonus).
/// 3. Scale through the caster's OutgoingDamage stat modifiers -- a modifier scoped to e.g.
///    Tag.Melee via StatModifierComponent.ConditionTag only contributes when the activating
///    action/item actually carries that tag (context.ActivatorTags, passed through here); this is
///    also how BodyPartEffectsSystem's own Arm/Hand penalty now works (see
///    PLAN-body-part-gameplay-effects.md), no longer a dedicated MeleeOutgoingDamage target.
/// 4. Roll a crit; on success, multiply the fully-damageWithTagModifiers result from step 3 by CritMultiplier --
///    crit is the last multiplier applied, matching Diablo/PoE's dominant convention (a crit
///    amplifies the fully-modified number, not a pre-buff base).
/// DoT damage (Poison/Burning) never goes through this entry at all -- PoisonSystem/BurningSystem
/// call HealthDamage.Apply directly on their own tick timers, so DoT damage deliberately never
/// rolls variance or crit.
/// </summary>
public sealed record DirectDamage(
    short MinFlatDamage,
    short MaxFlatDamage,
    float PercentageDamage = 0f,
    BodyPartType? TargetBodyPartType = null,
    BodyPartTargetMode BodyPartTargetMode = BodyPartTargetMode.SingleTarget) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        var flatBaseDamage = context.DamageOverride ?? (ushort)context.MathUtility.Next(MinFlatDamage, MaxFlatDamage + 1);

        var percentageBaseDamage = 0f;
        if (PercentageDamage > 0 && HealthQueries.TryGetEffectiveMaximum(context.Health, context.BodyParts, context.StatModifiers, context.TargetEntityId, out var effectiveMaxHealth))
        {
            percentageBaseDamage = PercentageDamage * effectiveMaxHealth;
        }

        var baseDamage = flatBaseDamage + percentageBaseDamage;
        if (baseDamage <= 0)
        {
            return;
        }

        var damageWithAbilityScoreScaling = baseDamage + AbilityScoreTagBonus.Compute(context.SourceEntityId, context.ActivatorTags, context.AbilityScores);
        var damageWithTagModifiers = StatModifierMath.GetEffectiveValue(context.StatModifiers, context.SourceEntityId, StatModifierTarget.OutgoingDamage, damageWithAbilityScoreScaling, context.ActivatorTags);

        var critChance = StatModifierMath.GetEffectiveValue(context.StatModifiers, context.SourceEntityId, StatModifierTarget.CritChance, CritMath.BaseCritChance);
        if (context.MathUtility.NextDouble() < critChance)
        {
            damageWithTagModifiers *= StatModifierMath.GetEffectiveValue(context.StatModifiers, context.SourceEntityId, StatModifierTarget.CritMultiplier, CritMath.BaseCritMultiplier);
        }

        BodyPartTargetRule? targetRule = TargetBodyPartType is { } type ? new BodyPartTargetRule(type, BodyPartFallback.Random) : null;
        HealthDamage.Apply(context.Health, context.EventBus, context.TargetEntityId, (ushort)damageWithTagModifiers, StatusEffectSource.FromEntity(context.SourceEntityId), context.PlayerQuery, context.ActivatorName, context.StatModifiers, context.BodyParts, context.MathUtility, context.DeadEntities, targetRule, context.ActivatorTags, BodyPartTargetMode);
    }
}
