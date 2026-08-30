using Game.Modules.StatModifiers;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;

namespace Game.Modules.Actions.Effects;

/// <summary>
/// Grants immunity to Type on context.TargetEntityId -- a hard on/off gate (StatusEffectImmunity.
/// IsImmune, checked by PoisonEffects.ApplyStack/BurningEffects.ApplyStack/BurningAuraApplier
/// before any stack is added), not a StatModifierComponent scale. DurationFrames (null = permanent)
/// is scaled the same way StatModifierGrant's own is -- context.DurationScaleMultiplier, then
/// Outgoing/IncomingBuffDuration (StatModifierGrant.ScaleDurationFrames, reused directly: granting
/// immunity is unambiguously a Buff from the target's perspective).
/// </summary>
public sealed record StatusEffectImmunityGrant(StatusEffectType Type, ushort? DurationFrames = null) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        var durationFrames = StatModifierGrant.ScaleDurationFrames(context, DurationFrames, StatModifierPolarity.Buff);

        context.ComponentManager.GetMultiPool<StatusEffectImmunityComponent>().Add(context.TargetEntityId, new StatusEffectImmunityComponent(Type, durationFrames));
    }
}
