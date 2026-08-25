using Engine.ECS.Components.Stores;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Microsoft.Xna.Framework;

namespace Game.Modules.Health;

/// <summary>Simple/Complex dispatching facade for healing by a fraction of max health -- the shared chokepoint DirectHeal, and any future heal effect, leans on.</summary>
/// <remarks>
/// Dispatches on which pool actually has entityId, mirroring HealthDamage.Apply: a
/// SimpleHealthComponent raises CurrentHealth by fraction of the modifier-effective
/// MaximumHealth, clamped there rather than the raw stored field (see StatModifierMath's own doc
/// comment for why). A BodyPartComponent-owning entity with no SimpleHealthComponent delegates to
/// ComplexHealthHeal.ApplyFractionToAllParts, which applies the same fraction independently to
/// every part's own MaximumHealth rather than one shared pool. Neither pool having entityId is a
/// no-op, same as HealthDamage.Apply.
/// </remarks>
public static class HealthHeal
{
    public static void Apply(
        PackedComponentPool<SimpleHealthComponent> health,
        int entityId,
        float fraction,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        MultiComponentPool<BodyPartComponent>? bodyParts = null)
    {
        if (!health.Has(entityId))
        {
            if (bodyParts?.Has(entityId) == true)
            {
                ComplexHealthHeal.ApplyFractionToAllParts(bodyParts, entityId, fraction, statModifiers);
            }

            return;
        }

        health.TryUpdate(entityId, (statModifiers, entityId, fraction), static (ref SimpleHealthComponent healthComponent, (MultiComponentPool<StatModifierComponent>? StatModifiers, int EntityId, float Fraction) state) =>
        {
            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(state.StatModifiers, state.EntityId, StatModifierTarget.MaximumHealth, healthComponent.MaximumHealth);
            healthComponent.CurrentHealth = MathHelper.Clamp(healthComponent.CurrentHealth + state.Fraction * effectiveMaximumHealth, 0f, effectiveMaximumHealth);
        });
    }
}
