using Engine.ECS.Components.Stores;
using Game.Modules.Actions.Components;

namespace Game.Modules.Actions;

/// <summary>Provides static methods for querying action instances.</summary>
/// <remarks>
/// MultiComponentPool has no built-in "get (or find-and-update) the instance matching field X"
/// accessor -- an entity owns one ActionInstanceComponent per action it knows, so the pool only
/// exposes a generic dense-chain walk plus predicate-based helpers
/// (TryGetFirst/TryUpdateFirst), deliberately blind to what ActionId means. This class owns the
/// "match by ActionId" predicate in one place instead of every caller (cooldown-setting code
/// included) re-writing the same chain-walk + inline predicate.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class ActionInstanceQueries
{
    /// <summary>Tries to get the action instance for a specific entity and action ID.</summary>
    /// <param name="instances">The pool of action instances</param>
    /// <param name="entityId">The ID of the entity for which to query instances</param>
    /// <param name="actionId">The ID of the action for which to query instances</param>
    /// <param name="instance">The action instance, if found</param>
    /// <returns>true if an instance was found; otherwise, false</returns>
    public static bool TryGet(MultiComponentPool<ActionInstanceComponent> instances, int entityId, Guid actionId, out ActionInstanceComponent instance) =>
        instances.TryGetFirst(entityId, actionId, static (ref readonly ActionInstanceComponent candidate, Guid id) => candidate.ActionId == id, out instance);

    /// <summary>Tries to set the cooldown for a specific action instance.</summary>
    /// <param name="instances">The pool of action instances</param>
    /// <param name="entityId">The ID of the entity for which to set the cooldown</param>
    /// <param name="actionId">The ID of the action for which to set the cooldown</param>
    /// <param name="cooldownFramesRemaining">The number of cooldown frames remaining</param>
    /// <returns>true if the cooldown was set; otherwise, false</returns>
    public static bool TrySetCooldown(MultiComponentPool<ActionInstanceComponent> instances, int entityId, Guid actionId, ushort cooldownFramesRemaining) =>
        instances.TryUpdateFirst(
            entityId,
            (actionId, cooldownFramesRemaining),
            static (ref readonly ActionInstanceComponent instance, (Guid ActionId, ushort CooldownFramesRemaining) state) => instance.ActionId == state.ActionId,
            static (ref ActionInstanceComponent instance, (Guid ActionId, ushort CooldownFramesRemaining) state) => instance.CooldownFramesRemaining = state.CooldownFramesRemaining);
}
