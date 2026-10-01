using Engine.ECS.Systems;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;

namespace Game.Effects.Entries;

/// <summary>
/// Grants immunity to Type on context.TargetEntityId -- a hard on/off gate (StatusEffectImmunity.
/// IsImmune, checked by PoisonEffects.ApplyStack/BurningEffects.ApplyStack/BurningApplier
/// before any stack is added), not a StatModifierComponent scale. DurationFrames (null = permanent)
/// is scaled the same way StatModifierGrant's own is -- context.DurationScaleMultiplier, then
/// Outgoing/IncomingBuffDuration (StatModifierGrant.ScaleDurationFrames, reused directly: granting
/// immunity is unambiguously a Buff from the target's perspective) -- and the scaled result is
/// turned into an absolute deadline from context.Now. Granted through
/// StatusEffectImmunityEffects.Grant, so immunity to a type the target already has is extended
/// rather than duplicated.
/// </summary>
public sealed record StatusEffectImmunityGrant(StatusEffectType Type, ushort? DurationFrames = null) : IEffectEntry
{
    public EffectOutcome Apply(in EffectContext context)
    {
        var durationFrames = StatModifierGrant.ScaleDurationFrames(in context, DurationFrames, StatModifierPolarity.Buff);
        var expiresAtFrame = durationFrames is { } frames ? FrameDeadline.After(context.Now, frames) : FrameDeadline.Never;

        StatusEffectImmunityEffects.Grant(context.Services.ComponentManager.GetMultiPool<StatusEffectImmunityComponent>(), context.TargetEntityId, Type, expiresAtFrame);
        return EffectOutcome.Applied;
    }
}
