using Game.Modules.StatModifiers;

namespace Game.Effects;

/// <summary>Scales the duration an effect entry grants, the one way every timed grant does.</summary>
/// <remarks>
/// context.DurationScaleMultiplier first (a scroll's Intelligence scaling; 1 otherwise), then the caster's
/// Outgoing and the target's Incoming Buff or Debuff duration, picked by polarity and tag-conditional through
/// context.ActivatorTags (EffectModifiers.Scale). Works in any unit -- frames or an effect's own ticks -- since it only
/// multiplies. A null or zero duration is returned unscaled.
/// </remarks>
public static class EffectDurations
{
    private static readonly (StatModifierTarget, StatModifierTarget)[] BuffDurationModifiers = [(StatModifierTarget.OutgoingBuffDuration, StatModifierTarget.IncomingBuffDuration)];

    private static readonly (StatModifierTarget, StatModifierTarget)[] DebuffDurationModifiers = [(StatModifierTarget.OutgoingDebuffDuration, StatModifierTarget.IncomingDebuffDuration)];

    /// <summary>The modifier pair that scales a duration of this polarity, as an entry's AmountModifiers lists it.</summary>
    public static IReadOnlyList<(StatModifierTarget Outgoing, StatModifierTarget Incoming)> ModifiersFor(StatModifierPolarity polarity) =>
        polarity == StatModifierPolarity.Buff ? BuffDurationModifiers : DebuffDurationModifiers;

    public static ushort? Scale(in EffectContext context, ushort? duration, StatModifierPolarity polarity)
    {
        if (duration is not { } value || value <= 0)
        {
            return duration;
        }

        var scaled = value * context.DurationScaleMultiplier;

        var outgoingTarget = polarity == StatModifierPolarity.Buff ? StatModifierTarget.OutgoingBuffDuration : StatModifierTarget.OutgoingDebuffDuration;
        var incomingTarget = polarity == StatModifierPolarity.Buff ? StatModifierTarget.IncomingBuffDuration : StatModifierTarget.IncomingDebuffDuration;

        return EffectModifiers.ScaleToUShort(in context, scaled, outgoingTarget, incomingTarget);
    }
}
