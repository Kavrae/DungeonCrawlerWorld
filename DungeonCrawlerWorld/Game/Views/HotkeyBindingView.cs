using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;

namespace Game.Views;

/// <summary>What each of an entity's hotkey slots is bound to, and how many Expansion slots it has unlocked.</summary>
public sealed class HotkeyBindingView(ComponentManager componentManager)
{
    private readonly MultiComponentPool<ActionHotkeyBindingComponent> _actionHotkeyBindings = componentManager.GetMultiPool<ActionHotkeyBindingComponent>();
    private readonly MultiComponentPool<ItemHotkeyBindingComponent> _itemHotkeyBindings = componentManager.GetMultiPool<ItemHotkeyBindingComponent>();
    private readonly PackedComponentPool<HotkeyExpansionUnlockComponent> _hotkeyExpansionUnlocks = componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>();

    /// <summary>The action slot is bound to, if it's bound to one.</summary>
    public bool TryGetBoundAction(int entityId, HotkeySlot slot, out Guid actionId) =>
        ActionHotkeyBindingQueries.TryGet(_actionHotkeyBindings, entityId, slot, out actionId);

    /// <summary>The stack slot is bound to, if it's bound to one.</summary>
    public bool TryGetBoundItem(int entityId, HotkeySlot slot, out uint stackInstanceId) =>
        ItemHotkeyBindingQueries.TryGet(_itemHotkeyBindings, entityId, slot, out stackInstanceId);

    /// <summary>How many Expansion slots entityId has unlocked, if it has ever unlocked any.</summary>
    public bool TryGetUnlockedExpansionSlots(int entityId, out short unlockedSlotCount)
    {
        var found = _hotkeyExpansionUnlocks.TryGetReadonly(entityId, out var unlock);
        unlockedSlotCount = found ? unlock.UnlockedSlotCount : (short)0;
        return found;
    }
}
