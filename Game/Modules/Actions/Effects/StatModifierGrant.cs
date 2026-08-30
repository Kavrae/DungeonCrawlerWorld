using Engine.Math;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.Actions.Effects;

/// <summary>
/// Grants one StatModifierComponent to context.TargetEntityId -- always the resolved target, no
/// separate Source/Target choice: a caller that wants to buff itself (e.g. a self-targeted
/// action) does so via a Self-shaped TargetingSpec, which already resolves TargetEntityId to the
/// caster, the same way every other effect entry reads "who this lands on" (see AuraSourceGrant's
/// own doc comment for the identical reasoning). No-op when context.StatModifiers isn't wired.
/// DurationFrames is scaled by context.DurationScaleMultiplier (a ScrollActivator activation sets
/// this off the caster's Intelligence -- see ScrollScalingEffects; every other activator leaves
/// it at the default 1.0, a no-op) -- guarded so DurationFrames' own Permanent sentinel (-1, see
/// StatModifierComponent) is never multiplied into a meaningless negative number. Then, still
/// guarded the same way, scaled through StatModifierMath against the caster's own
/// Outgoing{Buff,Debuff}Duration (context.SourceEntityId) and then the target's own
/// Incoming{Buff,Debuff}Duration (context.TargetEntityId) -- Polarity picks which of the two
/// pairs applies, both tag-conditional via context.ActivatorTags, exactly like DirectDamage's own
/// OutgoingDamage step.
/// </summary>
public sealed record StatModifierGrant(
    StatModifierTarget Target,
    StatModifierOperation Operation,
    StatModifierPolarity Polarity,
    bool CanModify,
    float Magnitude,
    ushort? DurationFrames,
    Tag? ConditionTag = null) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        if (context.StatModifiers is null)
        {
            return;
        }

        var durationFrames = ScaleDurationFrames(context, DurationFrames, Polarity);

        context.StatModifiers.Add(context.TargetEntityId, new StatModifierComponent(
            Target, Operation, Polarity, CanModify, Magnitude, durationFrames, StatusEffectSource.FromEntity(context.SourceEntityId), ConditionTag));
    }

    /// <summary>Shared by StatModifierGrant and StatusEffectImmunityGrant (granting immunity is unambiguously a Buff) -- context.DurationScaleMultiplier first, then Outgoing/IncomingBuffDuration or Outgoing/IncomingDebuffDuration (picked by polarity), both tag-conditional via context.ActivatorTags. A null or already-permanent (<= 0) durationFrames is returned unscaled, same guard as before.</summary>
    internal static ushort? ScaleDurationFrames(ActionEffectContext context, ushort? durationFrames, StatModifierPolarity polarity)
    {
        if (durationFrames is not { } value || value <= 0)
        {
            return durationFrames;
        }

        var scaled = value * context.DurationScaleMultiplier;

        var outgoingTarget = polarity == StatModifierPolarity.Buff ? StatModifierTarget.OutgoingBuffDuration : StatModifierTarget.OutgoingDebuffDuration;
        var incomingTarget = polarity == StatModifierPolarity.Buff ? StatModifierTarget.IncomingBuffDuration : StatModifierTarget.IncomingDebuffDuration;

        scaled = StatModifierMath.GetEffectiveValue(context.StatModifiers, context.SourceEntityId, outgoingTarget, scaled, context.ActivatorTags);
        scaled = StatModifierMath.GetEffectiveValue(context.StatModifiers, context.TargetEntityId, incomingTarget, scaled, context.ActivatorTags);

        return MathUtility.ClampUShort(scaled, 0, ushort.MaxValue);
    }
}
