using Engine.ECS.Components.Stores;
using Game.Modules.Abilities.Components;

namespace Game.Modules.Abilities;

/// <summary>Shared read helper for ActionHotkeyBindingComponent's MultiComponentPool -- same shape as AbilityInstanceQueries/StatusEffectQueries/ItemHotkeyBindingQueries, walking the dense per-entity chain to find the binding matching a given HotkeySlot.</summary>
public static class ActionHotkeyBindingQueries
{
    public static bool TryGet(MultiComponentPool<ActionHotkeyBindingComponent> bindings, int entityId, HotkeySlot slot, out Guid abilityId)
    {
        for (var denseIndex = bindings.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bindings.GetNextDenseIndex(denseIndex))
        {
            var candidate = bindings.GetReadonlyByDenseIndex(denseIndex);
            if (candidate.Slot == slot)
            {
                abilityId = candidate.AbilityId;
                return true;
            }
        }

        abilityId = default;
        return false;
    }

    /// <summary>Removes slot's binding, if any -- the action-side half of "a slot binds to at most one of {action, item} at a time" (see IHotkeySlotBinding's own doc comment). Used by HotbarContent.BindItem to clear the way before writing an item binding to the same slot.</summary>
    public static void Unbind(MultiComponentPool<ActionHotkeyBindingComponent> bindings, int entityId, HotkeySlot slot) =>
        bindings.RemoveFirst(entityId, slot, static (ref readonly binding, s) => binding.Slot == s);
}
