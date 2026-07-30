using Engine.ECS.Components.Stores;
using Game.Modules.Abilities.Components;

namespace Game.Modules.Abilities;

/// <summary>Shared read helper for AbilityInstanceComponent's MultiComponentPool -- same shape as StatusEffectQueries, walking the dense per-entity chain to find the granted instance matching a given AbilityId.</summary>
public static class AbilityInstanceQueries
{
    public static bool TryGet(MultiComponentPool<AbilityInstanceComponent> instances, int entityId, Guid abilityId, out AbilityInstanceComponent instance)
    {
        for (var denseIndex = instances.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = instances.GetNextDenseIndex(denseIndex))
        {
            var candidate = instances.GetReadonlyByDenseIndex(denseIndex);
            if (candidate.AbilityId == abilityId)
            {
                instance = candidate;
                return true;
            }
        }

        instance = default;
        return false;
    }

    /// <summary>Sets CooldownFramesRemaining on the granted instance matching abilityId, if the entity has one -- used to start an ability's own cooldown on activation (any ActionTimingCategory, see AbilityTiming.CooldownFrames).</summary>
    public static bool TrySetCooldown(MultiComponentPool<AbilityInstanceComponent> instances, int entityId, Guid abilityId, short cooldownFramesRemaining) =>
        instances.TryUpdateFirst(
            entityId,
            (abilityId, cooldownFramesRemaining),
            static (ref readonly AbilityInstanceComponent instance, (Guid AbilityId, short CooldownFramesRemaining) state) => instance.AbilityId == state.AbilityId,
            static (ref AbilityInstanceComponent instance, (Guid AbilityId, short CooldownFramesRemaining) state) => instance.CooldownFramesRemaining = state.CooldownFramesRemaining);
}
