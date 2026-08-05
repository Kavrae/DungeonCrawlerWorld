using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.Health;

/// <summary>Raises CurrentHealth, mirroring HealthDamage.Apply's shape in reverse -- clamped at the modifier-effective MaximumHealth (see StatModifierMath), not the raw stored field, so a permanent +max-HP buff still caps healing correctly. A no-op for an entity with no HealthComponent (e.g. an "immortal" entity), same as HealthDamage.Apply.</summary>
public static class HealthHeal
{
    public static void Apply(
        PackedComponentPool<HealthComponent> health,
        int entityId,
        short amount,
        MultiComponentPool<StatModifierComponent>? statModifiers = null)
    {
        if (!health.Has(entityId))
        {
            return;
        }

        health.TryUpdate(entityId, (statModifiers, entityId, amount), static (ref HealthComponent healthComponent, (MultiComponentPool<StatModifierComponent>? StatModifiers, int EntityId, short Amount) state) =>
        {
            var effectiveMaximumHealth = (short)StatModifierMath.GetEffectiveValue(state.StatModifiers, state.EntityId, StatModifierTarget.MaximumHealth, healthComponent.MaximumHealth);
            healthComponent.CurrentHealth = MathUtility.ClampShort((short)(healthComponent.CurrentHealth + state.Amount), 0, effectiveMaximumHealth);
        });
    }
}
