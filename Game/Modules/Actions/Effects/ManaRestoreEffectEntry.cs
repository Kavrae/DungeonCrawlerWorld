using Game.Modules.Mana;
using Game.Modules.StatModifiers;

namespace Game.Modules.Actions.Effects;

/// <summary>Mirrors HealEffectEntry exactly, against Mana instead of Health. No-op when context.Mana isn't wired, or the target has no ManaComponent (see ManaRestore.Apply's own doc comment).</summary>
public sealed record ManaRestoreEffectEntry(float Fraction) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        if (Fraction <= 0 || context.Mana is null || !context.Mana.TryGetReadonly(context.TargetEntityId, out var targetMana))
        {
            return;
        }

        var effectiveMaximumMana = StatModifierMath.GetEffectiveValue(context.StatModifiers, context.TargetEntityId, StatModifierTarget.MaximumMana, targetMana.MaximumMana);
        ManaRestore.Apply(context.Mana, context.TargetEntityId, (short)(Fraction * effectiveMaximumMana), context.StatModifiers);
    }
}
