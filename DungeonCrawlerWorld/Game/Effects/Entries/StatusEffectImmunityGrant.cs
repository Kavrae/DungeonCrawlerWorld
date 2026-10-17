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
/// <remarks>Permanent (DurationFrames null) and held by a toggle (context.HeldGrantKey set), it is an immunity of the toggle's own, taken back by Revert when the toggle goes off; any other immunity to the type stays.</remarks>
public sealed record StatusEffectImmunityGrant(StatusEffectType Type, ushort? DurationFrames = null) : IReversibleEffectEntry
{
    private static readonly (StatModifierTarget, StatModifierTarget)[] Modifiers = [(StatModifierTarget.OutgoingBuffDuration, StatModifierTarget.IncomingBuffDuration)];

    /// <remarks>The duration of a timed immunity.</remarks>
    public IReadOnlyList<(StatModifierTarget Outgoing, StatModifierTarget Incoming)> AmountModifiers => DurationFrames is null ? [] : Modifiers;

    public bool GrantsUntilRevoked => DurationFrames is null;

    public EffectOutcome Apply(in EffectContext context)
    {
        var immunities = context.Services.ComponentManager.GetMultiPool<StatusEffectImmunityComponent>();
        if (DurationFrames is null && context.HeldGrantKey is { } heldGrantKey)
        {
            StatusEffectImmunityEffects.GrantHeld(immunities, context.TargetEntityId, Type, heldGrantKey);
            return EffectOutcome.Applied;
        }

        var durationFrames = StatModifierGrant.ScaleDurationFrames(in context, DurationFrames, StatModifierPolarity.Buff);
        var expiresAtFrame = durationFrames is { } frames ? FrameDeadline.After(context.Now, frames) : FrameDeadline.Never;

        StatusEffectImmunityEffects.Grant(immunities, context.TargetEntityId, Type, expiresAtFrame);
        return EffectOutcome.Applied;
    }

    /// <remarks>A timed immunity ends by its own expiry, and a permanent one granted outside a toggle has no key to be taken back under.</remarks>
    public void Revert(in EffectContext context)
    {
        if (DurationFrames is null && context.HeldGrantKey is { } heldGrantKey)
        {
            StatusEffectImmunityEffects.RevokeHeld(context.Services.ComponentManager.GetMultiPool<StatusEffectImmunityComponent>(), context.TargetEntityId, Type, heldGrantKey);
        }
    }
}
