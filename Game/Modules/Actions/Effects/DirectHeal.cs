using Game.Modules.Health;

namespace Game.Modules.Actions.Effects;

/// <summary>Heals the target by a fraction of its own effective MaximumHealth (0.5f = 50%, 1f = a full restore) -- computed per target, not per caster, so a splash hitting entities with different maximums restores each by its own fraction.</summary>
public sealed record DirectHeal(float Fraction) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        if (Fraction <= 0)
        {
            return;
        }

        HealthHeal.Apply(context.Health, context.TargetEntityId, Fraction, context.StatModifiers, context.BodyParts);
    }
}
