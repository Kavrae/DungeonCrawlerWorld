using Game.Modules.Mana;
using Game.Modules.StatModifiers;

namespace Game.Effects.Entries;

/// <summary>Mirrors DirectHeal exactly, against Mana instead of Health. No-op when the target has no ManaComponent (see ManaRestore.Apply's own doc comment). Fraction is multiplied by context.Magnitude.</summary>
/// <remarks>The amount is never rounded: mana is held as a float. A target already at its effective maximum, or one whose modifiers cut the amount to nothing, is left alone and reported as NoEffect, as DirectHeal does.</remarks>
public sealed record DirectManaRestore(float Fraction) : IEffectEntry
{
    private static readonly (StatModifierTarget, StatModifierTarget)[] Modifiers = [(StatModifierTarget.OutgoingManaRestore, StatModifierTarget.IncomingManaRestore)];

    public IReadOnlyList<(StatModifierTarget Outgoing, StatModifierTarget Incoming)> AmountModifiers => Modifiers;

    public EffectOutcome Apply(in EffectContext context)
    {
        if (Fraction <= 0 || !context.Services.Mana.TryGetReadonly(context.TargetEntityId, out var targetMana))
        {
            return EffectOutcome.NoEffect;
        }

        var effectiveMaximumMana = StatModifierMath.GetEffectiveValue(context.Services.StatModifiers, context.TargetEntityId, StatModifierTarget.MaximumMana, targetMana.MaximumMana);
        if (targetMana.CurrentMana >= effectiveMaximumMana)
        {
            return EffectOutcome.NoEffect;
        }

        var amount = MathF.Max(0f, EffectModifiers.Scale(in context, Fraction * context.Magnitude * effectiveMaximumMana, StatModifierTarget.OutgoingManaRestore, StatModifierTarget.IncomingManaRestore));
        if (amount <= 0)
        {
            return EffectOutcome.NoEffect;
        }

        ManaRestore.Apply(context.Services.Mana, context.TargetEntityId, amount, context.Services.StatModifiers);
        return EffectOutcome.Applied;
    }
}
