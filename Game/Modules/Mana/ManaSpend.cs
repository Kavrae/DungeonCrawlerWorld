using Engine.ECS.Components.Stores;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Microsoft.Xna.Framework;

namespace Game.Modules.Mana;

/// <summary>Lowers CurrentMana, mirroring HealthDamage.Apply's clamping shape but without any of its death/event-publishing concerns -- running out of mana isn't a death condition. A no-op for an entity with no ManaComponent (e.g. one that has never gained a mana-costing ability).</summary>
public static class ManaSpend
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
            manaComponent.CurrentMana = MathHelper.Clamp(manaComponent.CurrentMana - state.Amount, 0f, effectiveMaximumMana);
        });
    }
}
