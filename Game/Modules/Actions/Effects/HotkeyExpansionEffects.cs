using Engine.ECS.Components.Stores;
using Game.Modules.Actions.Components;

namespace Game.Modules.Actions.Effects;

/// <summary>Write surface for permanently unlocking more Expansion hotkey slots -- mirrors HealthHeal/ManaRestore's shape (read-clamp-write via the pool directly, not ComponentManager.Merge, since this always increments an existing value rather than overwriting one). A no-op for an entity with no HotkeyExpansionUnlockComponent at all (nothing granted it one, e.g. a non-player entity), the same "immortal but affectable" no-op convention HealthHeal.Apply follows for HealthComponent.</summary>
public static class HotkeyExpansionEffects
{
    public const short MaxUnlockedSlots = 20;

    public static void Grant(PackedComponentPool<HotkeyExpansionUnlockComponent> hotkeyExpansionUnlocks, int entityId, short amount)
    {
        if (!hotkeyExpansionUnlocks.Has(entityId))
        {
            return;
        }

        hotkeyExpansionUnlocks.TryUpdate(entityId, amount, static (ref HotkeyExpansionUnlockComponent unlock, short grantAmount) =>
        {
            unlock.UnlockedSlotCount = (short)Math.Min(MaxUnlockedSlots, unlock.UnlockedSlotCount + grantAmount);
        });
    }
}
