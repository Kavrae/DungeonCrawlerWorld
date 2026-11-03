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
        float amount,
        MultiComponentPool<StatModifierComponent> statModifiers)
    {
        var denseIndex = mana.GetDenseIndex(entityId);
        if (denseIndex < 0)
        {
            return;
        }

        var effectiveMaximumMana = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumMana, mana.GetReadonlyByDenseIndex(denseIndex).MaximumMana);
        mana.UpdateByDenseIndex(denseIndex, new ShiftCurrentManaUpdate(amount, effectiveMaximumMana));
    }
}
