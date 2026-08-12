using Game.Modules.AbilityScores;
using Game.Modules.Health;
using Game.Modules.StatModifiers;
using Game.World;

namespace Game.Modules.Actions.Effects;

/// <summary>
/// Deals damage -- shared by actions and consumables alike (a thrown explosive deals damage
/// the same way a spell does; nothing here is action-specific). MinAmount/MaxAmount is rolled
/// unless context.DamageOverride is set (ActionInstanceComponent's per-instance, per-race
/// override -- see ActionEffectContext's own doc comment; consumables never set it, so they
/// always roll the range). Order of operations, itself an application of the "composition order
/// is meaningful" rule one level down:
/// 1. Base amount: DamageOverride if set, else a MinAmount..MaxAmount roll (inclusive).
/// 2. Add the caster's ability-score tag bonus (AbilityScoreTagBonus).
/// 3. Scale through the caster's OutgoingDamage stat modifiers.
/// 4. Roll a crit; on success, multiply the fully-scaled result from step 3 by CritMultiplier --
///    crit is the last multiplier applied, matching Diablo/PoE's dominant convention (a crit
///    amplifies the fully-modified number, not a pre-buff base).
/// DoT damage (Poison/Burning) never goes through this entry at all -- PoisonSystem/BurningSystem
/// call HealthDamage.Apply directly on their own tick timers, so DoT damage deliberately never
/// rolls variance or crit.
/// </summary>
public sealed record DamageEffectEntry(short MinAmount, short MaxAmount) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        var baseAmount = context.DamageOverride ?? (short)context.MathUtility.Next(MinAmount, MaxAmount + 1);
        if (baseAmount <= 0)
        {
            return;
        }

        var withBonus = baseAmount + AbilityScoreTagBonus.Compute(context.SourceEntityId, context.ActivatorTags, context.AbilityScores);
        var scaled = StatModifierMath.GetEffectiveValue(context.StatModifiers, context.SourceEntityId, StatModifierTarget.OutgoingDamage, withBonus);

        var critChance = StatModifierMath.GetEffectiveValue(context.StatModifiers, context.SourceEntityId, StatModifierTarget.CritChance, CritMath.BaseCritChance);
        if (context.MathUtility.NextDouble() < critChance)
        {
            scaled *= StatModifierMath.GetEffectiveValue(context.StatModifiers, context.SourceEntityId, StatModifierTarget.CritMultiplier, CritMath.BaseCritMultiplier);
        }

        HealthDamage.Apply(context.Health, context.EventBus, context.TargetEntityId, (short)scaled, StatusEffectSource.FromEntity(context.SourceEntityId), context.PlayerQuery, context.ActivatorName, context.StatModifiers);
    }
}
