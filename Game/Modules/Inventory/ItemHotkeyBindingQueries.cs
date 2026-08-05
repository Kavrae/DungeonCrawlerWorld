using Engine.ECS.Components.Stores;
using Game.Modules.Abilities;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory;

/// <summary>Shared read helper for ItemHotkeyBindingComponent's MultiComponentPool -- mirrors Game.Modules.Abilities.ActionHotkeyBindingQueries exactly, walking the dense per-entity chain to find the binding matching a given HotkeySlot.</summary>
public static class ItemHotkeyBindingQueries
{
    public static bool TryGet(MultiComponentPool<ItemHotkeyBindingComponent> bindings, int entityId, HotkeySlot slot, out Guid itemDefinitionId)
    {
        for (var denseIndex = bindings.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bindings.GetNextDenseIndex(denseIndex))
        {
            var candidate = bindings.GetReadonlyByDenseIndex(denseIndex);
            if (candidate.Slot == slot)
            {
                itemDefinitionId = candidate.ItemDefinitionId;
                return true;
            }
        }

        itemDefinitionId = default;
        return false;
    }

    /// <summary>Removes slot's binding, if any -- the item-side half of "a slot binds to at most one of {action, item} at a time" (see IHotkeySlotBinding's own doc comment). Used by HotbarContent.BindItem/UnbindItemSlot, the real drag-and-drop assignment path.</summary>
    public static void Unbind(MultiComponentPool<ItemHotkeyBindingComponent> bindings, int entityId, HotkeySlot slot) =>
        bindings.RemoveFirst(entityId, slot, static (ref readonly binding, s) => binding.Slot == s);
}
