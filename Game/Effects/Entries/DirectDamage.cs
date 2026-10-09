using Game.Modules.AbilityScores;
using Game.Modules.Health;
using Game.Modules.StatModifiers;
using Game.Resources;

namespace Game.Effects.Entries;

/// <summary>
/// Deals damage -- shared by actions and consumables alike (a thrown explosive deals damage
/// the same way a spell does; nothing here is action-specific). MinFlatDamage/MaxFlatDamage is
/// always rolled -- a per-race/per-instance fixed number (e.g. Goblin's Punch) is expressed by
/// granting an ActionInstanceComponent.Override whose DirectDamage entry sets
/// MinFlatDamage == MaxFlatDamage (see ActionOverrideEffects.OverrideFlatDamage), not a side
/// channel on the context. PercentageDamage (if set) adds a further percentage of the target's
/// own modifier-effective max health on top (HealthQueries.TryGetEffectiveMaximum), converted to
/// a flat number and added before anything else runs -- an execute-style effect ("+10% of target's
/// max health") doesn't need any special-casing further down the chain. Order of operations,
/// itself an application of the "composition order is meaningful" rule one level down:
/// 1. Base amount: a MinFlatDamage..MaxFlatDamage roll + PercentageDamage * target's effective max health.
/// 2. Add the caster's ability-score tag bonus (AbilityScoreTagBonus).
/// 3. Scale through the caster's OutgoingDamage stat modifiers -- a modifier scoped to e.g.
///    GameTags.DeliveryMelee via StatModifierComponent.ConditionTag only contributes when the activating
///    action/item actually carries that tag (context.ActivatorTags, passed through here); this is
///    also how BodyPartEffectsSystem's own Arm/Hand penalty now works, no longer a dedicated
///    MeleeOutgoingDamage target.
/// 4. Roll a crit; on success, multiply the fully-damageWithTagModifiers result from step 3 by CritMultiplier --
///    crit is the last multiplier applied, matching Diablo/PoE's dominant convention (a crit
///    amplifies the fully-modified number, not a pre-buff base).
/// The flat roll is multiplied by context.Magnitude (an aura's power at the target; 1 otherwise).
/// BodyPart says which part it lands on; Unspecified leaves it to BodyPartTargetMode (one part at
/// random, or every part).
/// Steps 2 to 4 read the source entity, so with none (context.SourceEntityId null: terrain, an
/// aura) they are skipped: the damage is the base amount, never a crit.
/// DoT damage (Poison/Burning) never goes through this entry at all -- PoisonSystem/BurningSystem
/// call HealthDamage.Apply directly on their own tick timers, so DoT damage deliberately never
/// rolls variance or crit.
/// </summary>
public sealed record DirectDamage(
    short MinFlatDamage,
    short MaxFlatDamage,
    float PercentageDamage = 0f,
    BodyPartTargeting BodyPart = default,
    BodyPartTargetMode BodyPartTargetMode = BodyPartTargetMode.SingleTarget) : IEffectEntry
{
    /// <remarks>Incoming is applied at HealthDamage, the chokepoint damage over time shares.</remarks>
    private static readonly (StatModifierTarget, StatModifierTarget)[] Modifiers = [(StatModifierTarget.OutgoingDamage, StatModifierTarget.IncomingDamage)];

    public IReadOnlyList<(StatModifierTarget Outgoing, StatModifierTarget Incoming)> AmountModifiers => Modifiers;

    public EffectOutcome Apply(in EffectContext context)
    {
        var flatRoll = MinFlatDamage == MaxFlatDamage ? MinFlatDamage : context.Services.MathUtility.Next(MinFlatDamage, MaxFlatDamage + 1);
        var flatBaseDamage = (ushort)flatRoll * context.Magnitude;

        var percentageBaseDamage = 0f;
        if (PercentageDamage > 0 && HealthQueries.TryGetEffectiveMaximum(context.Services.Health, context.Services.BodyParts, context.Services.StatModifiers, context.TargetEntityId, out var effectiveMaxHealth))
        {
            percentageBaseDamage = PercentageDamage * effectiveMaxHealth;
        }

        var baseDamage = flatBaseDamage + percentageBaseDamage;
        if (baseDamage <= 0)
        {
            return EffectOutcome.NoEffect;
        }

        var damageWithTagModifiers = baseDamage;
        var isCritical = false;

        if (context.SourceEntityId is { } sourceEntityId)
        {
            var statModifiers = context.Services.StatModifiers;
            var damageWithAbilityScoreScaling = baseDamage + AbilityScoreTagBonus.Compute(sourceEntityId, context.ActivatorTags, context.Services.AbilityScores);
            damageWithTagModifiers = EffectModifiers.ScaleOutgoing(in context, damageWithAbilityScoreScaling, StatModifierTarget.OutgoingDamage);

            var critChance = StatModifierMath.GetEffectiveValue(statModifiers, sourceEntityId, StatModifierTarget.CritChance, CritMath.BaseCritChance);
            isCritical = context.Services.MathUtility.NextDouble() < critChance;
            if (isCritical)
            {
                damageWithTagModifiers *= StatModifierMath.GetEffectiveValue(statModifiers, sourceEntityId, StatModifierTarget.CritMultiplier, CritMath.BaseCritMultiplier);
            }
        }

        var targetRule = BodyPart.ResolveRule(in context);
        HealthDamage.Apply(context.Services.Health, context.Services.EventBus, context.TargetEntityId, damageWithTagModifiers, context.Source, context.Services.PlayerQuery, context.ActivatorName, context.Now, context.Services.StatModifiers, context.Services.BodyParts, context.Services.MathUtility, context.Services.DeadEntities, context.Services.DamageLedger, context.Services.FloatingTextFeed, ResourceLossCategory.Direct, targetRule, context.ActivatorTags, BodyPartTargetMode, isCritical);
        return EffectOutcome.Applied;
    }
}
