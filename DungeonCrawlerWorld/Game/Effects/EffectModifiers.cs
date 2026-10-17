using Engine.Math;
using Game.Modules.StatModifiers;

namespace Game.Effects;

/// <summary>Scales an amount an effect entry carries through the stat modifiers on both ends: Outgoing on whoever causes it, Incoming on whoever it lands on.</summary>
/// <remarks>
/// The one helper every entry with an amount runs: damage (Outgoing here, Incoming at HealthDamage's
/// chokepoint, which DoTs share), durations, mana restored and drained, status stacks, proc chance, and
/// an aura's power and size. Modifiers are read live, at application -- a Delayed activation's are
/// the ones in effect when its windup ends. Outgoing is skipped when the effect has no source entity
/// (terrain, an aura), and Incoming when there is no target entity (an entry placed on a tile).
/// Both are conditional on the activator's tags (context.ActivatorTags). Health and mana -- damage, healing,
/// mana drained and restored, as a cost or not -- are held as floats and never rounded, so no modifier can
/// round a cost down to free or a hit down to nothing. Only an amount that must be a whole number (status
/// stacks, durations in frames, an aura's power and size) is rounded to the nearest value and clamped to its
/// type (ScaleToUShort, ScaleToByte), never truncated.
/// </remarks>
public static class EffectModifiers
{
    /// <summary>Scales amount through Outgoing modifiers on context.SourceEntityId, then Incoming modifiers on context.TargetEntityId.</summary>
    public static float Scale(in EffectContext context, float amount, StatModifierTarget outgoing, StatModifierTarget incoming)
    {
        var scaled = ScaleOutgoing(in context, amount, outgoing);

        return context.TargetEntityId == EffectContext.NoTargetEntity
            ? scaled
            : StatModifierMath.GetEffectiveValue(context.Services.StatModifiers, context.TargetEntityId, incoming, scaled, context.ActivatorTags);
    }

    /// <summary>Scales amount as Scale does, rounded to the nearest whole number and clamped to 0..ushort.MaxValue.</summary>
    public static ushort ScaleToUShort(in EffectContext context, float amount, StatModifierTarget outgoing, StatModifierTarget incoming) =>
        RoundToUShort(Scale(in context, amount, outgoing, incoming));

    /// <summary>Scales amount as Scale does, rounded to the nearest whole number and clamped to 0..byte.MaxValue.</summary>
    public static byte ScaleToByte(in EffectContext context, float amount, StatModifierTarget outgoing, StatModifierTarget incoming) =>
        (byte)Math.Clamp(MathF.Round(Scale(in context, amount, outgoing, incoming)), 0f, byte.MaxValue);

    /// <summary>Scales amount through Outgoing modifiers on context.SourceEntityId alone, for an amount whose Incoming pass is applied further on (damage, at HealthDamage).</summary>
    public static float ScaleOutgoing(in EffectContext context, float amount, StatModifierTarget outgoing) =>
        context.SourceEntityId is { } sourceEntityId
            ? StatModifierMath.GetEffectiveValue(context.Services.StatModifiers, sourceEntityId, outgoing, amount, context.ActivatorTags)
            : amount;

    /// <summary>amount rounded to the nearest whole number and clamped to 0..ushort.MaxValue: how every scaled whole-number amount is made one.</summary>
    public static ushort RoundToUShort(float amount) => MathUtility.ClampUShort(MathF.Round(amount), 0, ushort.MaxValue);
}
