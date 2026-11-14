using Engine.ECS.Components.Stores;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Microsoft.Xna.Framework;

namespace Game.Modules.Mana;

/// <summary>Mana taken as a cost (ManaDrain): asked first, so a use is refused rather than taking more than is there.</summary>
/// <remarks>
/// Mana's counterpart to HealthCost. Running out of mana is no death condition, so taken without asking first it
/// simply stops at 0. It publishes no event and shows no floating text.
/// </remarks>
public static class ManaCost
{
    /// <summary>Whether entityId has a mana pool to pay with at all.</summary>
    public static bool HasMana(PackedComponentPool<ManaComponent> mana, int entityId) =>
        mana.Has(entityId);

    /// <summary>Whether entityId has at least amount mana left.</summary>
    public static bool HasEnough(PackedComponentPool<ManaComponent> mana, int entityId, float amount) =>
        mana.TryGetReadonly(entityId, out var manaComponent) && manaComponent.CurrentMana >= amount;

    /// <summary>Takes amount from entityId's CurrentMana, down to 0 at the least and its modified maximum at the most.</summary>
    /// <remarks>A no-op for an entity with no ManaComponent.</remarks>
    public static void Take(
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
        mana.UpdateByDenseIndex(denseIndex, new ShiftCurrentManaUpdate(-amount, effectiveMaximumMana));
    }
}
