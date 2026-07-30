using Engine.ECS.Components.Stores;
using Game.Modules.Abilities.Components;

namespace Game.Modules.Abilities;

/// <summary>Shared read helper for HotkeyBindingComponent's MultiComponentPool -- same shape as AbilityInstanceQueries/StatusEffectQueries, walking the dense per-entity chain to find the binding matching a given HotkeySlot.</summary>
public static class HotkeyBindingQueries
{
    public static bool TryGet(MultiComponentPool<HotkeyBindingComponent> bindings, int entityId, HotkeySlot slot, out Guid abilityId)
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
}
