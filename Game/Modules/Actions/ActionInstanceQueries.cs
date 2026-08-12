using Engine.ECS.Components.Stores;
using Game.Modules.Actions.Components;

namespace Game.Modules.Actions;

/// <summary>Shared read helper for ActionInstanceComponent's MultiComponentPool -- same shape as StatusEffectQueries, walking the dense per-entity chain to find the granted instance matching a given ActionId.</summary>
public static class ActionInstanceQueries
{
    public static bool TryGet(MultiComponentPool<ActionInstanceComponent> instances, int entityId, Guid actionId, out ActionInstanceComponent instance)
    {
        for (var denseIndex = instances.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = instances.GetNextDenseIndex(denseIndex))
        {
            var candidate = instances.GetReadonlyByDenseIndex(denseIndex);
            if (candidate.ActionId == actionId)
            {
                instance = candidate;
                return true;
            }
        }

        instance = default;
        return false;
    }

    /// <summary>Sets CooldownFramesRemaining on the granted instance matching actionId, if the entity has one -- used to start an action's own cooldown on activation (any ActionTimingCategory, see ActionTiming.CooldownFrames).</summary>
    public static bool TrySetCooldown(MultiComponentPool<ActionInstanceComponent> instances, int entityId, Guid actionId, short cooldownFramesRemaining) =>
        instances.TryUpdateFirst(
            entityId,
            (actionId, cooldownFramesRemaining),
            static (ref readonly ActionInstanceComponent instance, (Guid ActionId, short CooldownFramesRemaining) state) => instance.ActionId == state.ActionId,
            static (ref ActionInstanceComponent instance, (Guid ActionId, short CooldownFramesRemaining) state) => instance.CooldownFramesRemaining = state.CooldownFramesRemaining);
}
