using Engine.ECS.Components.Stores;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.Mana;

/// <summary>The one place an entity's mana maximum is read: with its MaximumMana modifiers applied, or its base under its own name.</summary>
public static class ManaQueries
{
    /// <summary>Its current mana and its maximum with its MaximumMana modifiers applied; false for an entity with no mana.</summary>
    public static bool TryGetTotals(PackedComponentPool<ManaComponent> mana, MultiComponentPool<StatModifierComponent> statModifiers, int entityId, out float current, out float maximum)
    {
        if (mana.TryGetReadonly(entityId, out var component))
        {
            current = component.CurrentMana;
            maximum = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumMana, component.BaseMaximumMana);
            return true;
        }

        current = 0f;
        maximum = 0f;
        return false;
    }

    /// <summary>Its maximum mana before any MaximumMana modifier; false for an entity with no mana.</summary>
    /// <remarks>For what changes the base itself (a race applied or changed while modifiers are active), never for comparing with current mana.</remarks>
    public static bool TryGetBaseMaximum(PackedComponentPool<ManaComponent> mana, int entityId, out float baseMaximum)
    {
        if (mana.TryGetReadonly(entityId, out var component))
        {
            baseMaximum = component.BaseMaximumMana;
            return true;
        }

        baseMaximum = 0f;
        return false;
    }

    /// <summary>The modified maximum of the mana component at denseIndex, for a writer that already holds its slot.</summary>
    internal static float EffectiveMaximumAt(PackedComponentPool<ManaComponent> mana, MultiComponentPool<StatModifierComponent> statModifiers, int entityId, int denseIndex) =>
        StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumMana, mana.GetReadonlyByDenseIndex(denseIndex).BaseMaximumMana);
}
