using Engine.ECS.Components.Stores;
using Game.Modules.Actions.Components;

namespace Game.Modules.Actions;

/// <summary>Provides static methods for querying action hotkey bindings.</summary>
/// <remarks>
/// Thin, better-named wrapper over the generic HotkeySlotBindingQueries (mirrors
/// Game.Modules.Inventory.ItemHotkeyBindingQueries exactly, both backed by the same generic
/// implementation) -- callers here get an "actionId" out param rather than the generic BoundId.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class ActionHotkeyBindingQueries
{
    /// <summary>Tries to get the action ID bound to a specific hotkey slot for an entity.</summary>
    /// <param name="bindings">The pool of action hotkey bindings</param>
    /// <param name="entityId">The ID of the entity for which to query bindings</param>
    /// <param name="slot">The hotkey slot to query</param>
    /// <param name="actionId">The ID of the action bound to the slot, if found</param>
    /// <returns>true if a binding was found; otherwise, false</returns>
    public static bool TryGet(MultiComponentPool<ActionHotkeyBindingComponent> bindings, int entityId, HotkeySlot slot, out Guid actionId) =>
        HotkeySlotBindingQueries.TryGet(bindings, entityId, slot, out actionId);

    /// <summary>Removes the action hotkey binding for a specific entity and slot.</summary>
    /// <param name="bindings">The pool of action hotkey bindings</param>
    /// <param name="entityId">The ID of the entity for which to remove the binding</param>
    /// <param name="slot">The hotkey slot to unbind</param>
    public static void Unbind(MultiComponentPool<ActionHotkeyBindingComponent> bindings, int entityId, HotkeySlot slot) =>
        HotkeySlotBindingQueries.Unbind(bindings, entityId, slot);
}
