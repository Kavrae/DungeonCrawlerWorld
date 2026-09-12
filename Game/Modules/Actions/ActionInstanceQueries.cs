using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
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

    /// <summary>
    /// Resolves the effective ActionDefinition for a granted instance -- instance.Override if the
    /// grant diverged from the catalog original, else a plain catalog lookup by ActionId. Mirrors
    /// InventoryQueries.TryResolveEffectiveItem exactly; see ActionInstanceComponent.Override's own
    /// doc comment for why this is the one place every activation path should resolve through
    /// instead of calling ActionCatalog.TryGet directly.
    /// </summary>
    public static bool TryResolveEffectiveAction(ActionCatalog actionCatalog, in ActionInstanceComponent instance, out ActionDefinition definition)
    {
        if (instance.Override is { } overrideDefinition)
        {
            definition = overrideDefinition;
            return true;
        }

        return actionCatalog.TryGet(instance.ActionId, out definition!);
    }

    /// <summary>Starts a specific action instance's cooldown: usable again cooldownFrames frames after now.</summary>
    /// <param name="instances">The pool of action instances</param>
    /// <param name="entityId">The ID of the entity for which to set the cooldown</param>
    /// <param name="actionId">The ID of the action for which to set the cooldown</param>
    /// <param name="cooldownFrames">How many frames the cooldown lasts</param>
    /// <param name="now">The current simulation frame</param>
    /// <returns>true if the cooldown was set; otherwise, false</returns>
    public static bool TrySetCooldown(MultiComponentPool<ActionInstanceComponent> instances, int entityId, Guid actionId, ushort cooldownFrames, long now) =>
        instances.TryUpdateFirst(
            entityId,
            (actionId, ReadyAt: FrameDeadline.After(now, cooldownFrames)),
            static (ref readonly ActionInstanceComponent instance, (Guid ActionId, uint ReadyAt) state) => instance.ActionId == state.ActionId,
            static (ref ActionInstanceComponent instance, (Guid ActionId, uint ReadyAt) state) => instance.CooldownReadyAtFrame = state.ReadyAt);

    /// <summary>True while the instance's cooldown is still running at frame now.</summary>
    public static bool IsOnCooldown(in ActionInstanceComponent instance, long now) =>
        !FrameDeadline.IsReached(instance.CooldownReadyAtFrame, now);

    /// <summary>Frames left on the instance's cooldown at frame now; 0 when ready.</summary>
    public static int CooldownFramesRemaining(in ActionInstanceComponent instance, long now) =>
        FrameDeadline.Remaining(instance.CooldownReadyAtFrame, now);
}
