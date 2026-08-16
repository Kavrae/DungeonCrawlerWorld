using Engine.ECS.Components.Stores;
using Game.Modules.Actions.Components;

namespace Game.Modules.Actions;

/// <summary>Provides static methods for querying action hotkey bindings.</summary>
/// <remarks>
/// MultiComponentPool has no built-in "get the instance matching field X" accessor -- an entity
/// may own several bindings at once, so the pool only exposes a generic dense-chain walk plus
/// predicate-based helpers (TryGetFirst/RemoveFirst), deliberately blind to what an
/// ActionHotkeyBindingComponent's fields mean. This class owns the "match by HotkeySlot"
/// predicate in one place (mirrors Game.Modules.Inventory.ItemHotkeyBindingQueries exactly)
/// instead of every caller re-writing the same chain-walk + inline predicate.
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
    public static bool TryGet(MultiComponentPool<ActionHotkeyBindingComponent> bindings, int entityId, HotkeySlot slot, out Guid actionId)
    {
        var found = bindings.TryGetFirst(entityId, slot, static (ref readonly ActionHotkeyBindingComponent candidate, HotkeySlot s) => candidate.Slot == s, out var binding);
        actionId = found ? binding.ActionId : default;
        return found;
    }

    /// <summary>Removes the action hotkey binding for a specific entity and slot.</summary>
    /// <param name="bindings">The pool of action hotkey bindings</param>
    /// <param name="entityId">The ID of the entity for which to remove the binding</param>
    /// <param name="slot">The hotkey slot to unbind</param>
    public static void Unbind(MultiComponentPool<ActionHotkeyBindingComponent> bindings, int entityId, HotkeySlot slot) =>
        bindings.RemoveFirst(entityId, slot, static (ref readonly binding, s) => binding.Slot == s);
}
