using Game.Modules.Health;
using Game.Modules.StatModifiers;

namespace Game.Modules.Actions.Effects;

/// <summary>
/// Heals the target by a fraction of its own effective MaximumHealth (0.5f = 50%, 1f = a full
/// restore) -- computed per target, not per caster, so a splash hitting entities with different
/// maximums restores each by its own fraction. No-op for a target with no HealthComponent (see
/// HealthHeal.Apply's own doc comment).
/// </summary>
public sealed record HealEffectEntry(float Fraction) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        if (Fraction <= 0 || !context.Health.TryGetReadonly(context.TargetEntityId, out var targetHealth))
        {
            return;
        }

        var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(context.StatModifiers, context.TargetEntityId, StatModifierTarget.MaximumHealth, targetHealth.MaximumHealth);
        HealthHeal.Apply(context.Health, context.TargetEntityId, (short)(Fraction * effectiveMaximumHealth), context.StatModifiers);
    }
}
