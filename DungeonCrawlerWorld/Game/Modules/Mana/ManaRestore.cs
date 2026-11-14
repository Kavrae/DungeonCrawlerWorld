using Engine.ECS.Components.Stores;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Microsoft.Xna.Framework;

namespace Game.Modules.Mana;

/// <summary>Raises CurrentMana, mirroring HealthHeal.Apply's shape -- clamped at its modified maximum (ManaQueries), never BaseMaximumMana. A no-op for an entity with no ManaComponent.</summary>
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

        var effectiveMaximumMana = ManaQueries.EffectiveMaximumAt(mana, statModifiers, entityId, denseIndex);
        mana.UpdateByDenseIndex(denseIndex, new ShiftCurrentManaUpdate(amount, effectiveMaximumMana));
    }
}
