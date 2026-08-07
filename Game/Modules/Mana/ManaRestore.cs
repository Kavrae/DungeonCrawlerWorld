using Engine.ECS.Components.Stores;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Microsoft.Xna.Framework;

namespace Game.Modules.Mana;

/// <summary>Raises CurrentMana, mirroring HealthHeal.Apply's shape exactly -- clamped at the modifier-effective MaximumMana, not the raw stored field. A no-op for an entity with no ManaComponent.</summary>
public static class ManaRestore
{
    public static void Apply(
        PackedComponentPool<ManaComponent> mana,
        int entityId,
        short amount,
        MultiComponentPool<StatModifierComponent>? statModifiers = null)
    {
        if (!mana.Has(entityId))
        {
            return;
        }

        mana.TryUpdate(entityId, (statModifiers, entityId, amount), static (ref ManaComponent manaComponent, (MultiComponentPool<StatModifierComponent>? StatModifiers, int EntityId, short Amount) state) =>
        {
            var effectiveMaximumMana = StatModifierMath.GetEffectiveValue(state.StatModifiers, state.EntityId, StatModifierTarget.MaximumMana, manaComponent.MaximumMana);
            manaComponent.CurrentMana = MathHelper.Clamp(manaComponent.CurrentMana + state.Amount, 0f, effectiveMaximumMana);
        });
    }
}
