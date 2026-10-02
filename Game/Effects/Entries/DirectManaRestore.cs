using Game.Modules.Mana;
using Game.Modules.StatModifiers;

namespace Game.Effects.Entries;

/// <summary>Mirrors DirectHeal exactly, against Mana instead of Health. No-op when the target has no ManaComponent (see ManaRestore.Apply's own doc comment). Fraction is multiplied by context.Magnitude.</summary>
public sealed record DirectManaRestore(float Fraction) : IEffectEntry
{
    public EffectOutcome Apply(in EffectContext context)
    {
        if (Fraction <= 0 || !context.Services.Mana.TryGetReadonly(context.TargetEntityId, out var targetMana))
        {
            return EffectOutcome.NoEffect;
        }

        var effectiveMaximumMana = StatModifierMath.GetEffectiveValue(context.Services.StatModifiers, context.TargetEntityId, StatModifierTarget.MaximumMana, targetMana.MaximumMana);
        ManaRestore.Apply(context.Services.Mana, context.TargetEntityId, (short)(Fraction * context.Magnitude * effectiveMaximumMana), context.Services.StatModifiers);
        return EffectOutcome.Applied;
    }
}
