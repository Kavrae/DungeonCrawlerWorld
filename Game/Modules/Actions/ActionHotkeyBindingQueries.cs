using Engine.ECS.Components.Stores;
using Game.Modules.Actions.Components;

namespace Game.Modules.Actions;

/// <summary>Shared read helper for ActionHotkeyBindingComponent's MultiComponentPool -- same shape as ActionInstanceQueries/StatusEffectQueries/ItemHotkeyBindingQueries, walking the dense per-entity chain to find the binding matching a given HotkeySlot.</summary>
public static class ActionHotkeyBindingQueries
{
    public static bool TryGet(MultiComponentPool<ActionHotkeyBindingComponent> bindings, int entityId, HotkeySlot slot, out Guid actionId)
    {
        for (var denseIndex = bindings.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bindings.GetNextDenseIndex(denseIndex))
        {
            var candidate = bindings.GetReadonlyByDenseIndex(denseIndex);
            if (candidate.Slot == slot)
            {
                actionId = candidate.ActionId;
                return true;
            }
        }

        actionId = default;
        return false;
    }

    /// <summary>Removes slot's binding, if any -- the action-side half of "a slot binds to at most one of {action, item} at a time" (see IHotkeySlotBinding's own doc comment). Used by HotbarContent.BindItem to clear the way before writing an item binding to the same slot.</summary>
    public static void Unbind(MultiComponentPool<ActionHotkeyBindingComponent> bindings, int entityId, HotkeySlot slot) =>
        bindings.RemoveFirst(entityId, slot, static (ref readonly binding, s) => binding.Slot == s);
}
