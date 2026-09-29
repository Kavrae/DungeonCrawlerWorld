using Engine.ECS.Components.Stores;
using Game.Modules.Actions;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory;

/// <summary>Provides static query methods for working with item hotkey bindings.</summary>
/// <remarks>
/// Thin, better-named wrapper over the generic HotkeySlotBindingQueries (mirrors
/// Game.Modules.Actions.ActionHotkeyBindingQueries exactly, both backed by the same generic
/// implementation) -- callers here get an "itemDefinitionId" out param rather than the generic
/// BoundId.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class ItemHotkeyBindingQueries
{
    public static bool TryGet(MultiComponentPool<ItemHotkeyBindingComponent> bindings, int entityId, HotkeySlot slot, out uint stackInstanceId) =>
        HotkeySlotBindingQueries.TryGet<ItemHotkeyBindingComponent, uint>(bindings, entityId, slot, out stackInstanceId);

    /// <summary>Unbinds the item from the specified hotkey slot, if it is bound.</summary>
    /// <param name="bindings">The pool of item hotkey bindings.</param>
    /// <param name="entityId">The ID of the entity whose binding to unbind.</param>
    /// <param name="slot">The hotkey slot to unbind.</param>
    /// <summary>Whether an item can be bound to a hotbar slot at all; false for a loot box, since activating one opens every loot box held rather than that one.</summary>
    public static bool CanBind(ItemDefinition definition) => !definition.Tags.Contains(Tag.Lootbox);

    public static void Unbind(MultiComponentPool<ItemHotkeyBindingComponent> bindings, int entityId, HotkeySlot slot) =>
        HotkeySlotBindingQueries.Unbind<ItemHotkeyBindingComponent, uint>(bindings, entityId, slot);
}
