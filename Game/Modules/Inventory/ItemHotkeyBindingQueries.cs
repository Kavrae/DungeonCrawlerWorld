using Engine.ECS.Components.Stores;
using Game.Modules.Actions;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory;

/// <summary>Provides static query methods for working with item hotkey bindings.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class ItemHotkeyBindingQueries
{
    public static bool TryGet(MultiComponentPool<ItemHotkeyBindingComponent> bindings, int entityId, HotkeySlot slot, out Guid itemDefinitionId)
    {
        var found = bindings.TryGetFirst(entityId, slot, static (ref readonly ItemHotkeyBindingComponent candidate, HotkeySlot s) => candidate.Slot == s, out var binding);
        itemDefinitionId = found ? binding.ItemDefinitionId : default;
        return found;
    }

    /// <summary>Unbinds the item from the specified hotkey slot, if it is bound.</summary>
    /// <param name="bindings">The pool of item hotkey bindings.</param>
    /// <param name="entityId">The ID of the entity whose binding to unbind.</param>
    /// <param name="slot">The hotkey slot to unbind.</param>
    public static void Unbind(MultiComponentPool<ItemHotkeyBindingComponent> bindings, int entityId, HotkeySlot slot) =>
        bindings.RemoveFirst(entityId, slot, static (ref readonly binding, s) => binding.Slot == s);
}
