using Engine.ECS.Components.Stores;
using Game.Modules.Actions.Components;

namespace Game.Modules.Actions.Effects;

/// <summary>Write surface for permanently unlocking more Expansion hotkey slots. Method named Apply, not Grant, so it doesn't collide with HotkeyExpansionGrant's own name -- the entry is a noun (a grant), this is the verb performed on it, the same split every other *Effects write-surface uses (see StatModifierEffects.Apply).</summary>
public static class HotkeyExpansion
{
    public const byte MaxUnlockedSlots = 20;

    public static void Apply(PackedComponentPool<HotkeyExpansionUnlockComponent> hotkeyExpansionUnlocks, int entityId, byte amount)
    {
        if (!hotkeyExpansionUnlocks.Has(entityId))
        {
            return;
        }

        hotkeyExpansionUnlocks.TryUpdate(entityId, amount, static (ref HotkeyExpansionUnlockComponent unlock, byte grantAmount) =>
        {
            unlock.UnlockedSlotCount = (byte)Math.Min(MaxUnlockedSlots, unlock.UnlockedSlotCount + grantAmount);
        });
    }
}
