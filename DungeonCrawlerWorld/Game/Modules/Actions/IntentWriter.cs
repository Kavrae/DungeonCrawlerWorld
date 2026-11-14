using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Actions.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;

namespace Game.Modules.Actions;

/// <summary>The one way anything queues an entity's turn: an action or item activation, or a step.</summary>
/// <remarks>
/// A turn is one thing: queuing an activation withdraws the other kind's pending request and clears the entity's step,
/// so no entity steps and acts on the same frame, whatever its timing (FreeCast included); a step withdraws both
/// pending activations. The one step that goes with an activation -- the one it causes, Dodge's -- is written once the
/// activation is used (StepFromActivation), and touches no pending request. Nothing else writes a pending activation
/// or a step, so the rule holds by structure rather than by every caller remembering it.
///
/// Every write is one a later worker-threaded decision could record and apply: an add or remove, and the shared step
/// updater. An entity without a MovementComponent (a stationary creature) queues the same way: the step it would clear
/// isn't there. An NPC's idle wait isn't a turn: it is the decision's own (NpcDecisionContext.Idle).
/// </remarks>
public sealed class IntentWriter(
    PackedComponentPool<MovementComponent> movements,
    PackedComponentPool<PendingActionActivationComponent> pendingActions,
    PackedComponentPool<PendingItemActivationComponent> pendingItemActivations)
{
    /// <summary>An IntentWriter over componentManager's movement and pending activation pools.</summary>
    public static IntentWriter For(ComponentManager componentManager) => new(
        componentManager.GetPackedPool<MovementComponent>(),
        componentManager.GetPackedPool<PendingActionActivationComponent>(),
        componentManager.GetPackedPool<PendingItemActivationComponent>());

    /// <summary>Queues entityId's activation of actionId against selection, replacing any pending request and clearing its step.</summary>
    public void QueueAction(int entityId, Guid actionId, TargetSelection selection)
    {
        pendingItemActivations.Remove(entityId);
        pendingActions.Merge(entityId, new PendingActionActivationComponent(actionId, selection));
        movements.TryUpdate(entityId, new SetNextMapPositionUpdate(null));
    }

    /// <summary>Queues entityId's activation of its stack stackInstanceId against selection, replacing any pending request and clearing its step.</summary>
    /// <param name="activatedFromSlot">The hotkey slot it came from, if any: the slot that follows the unit when activating it moves it to another stack.</param>
    public void QueueItemActivation(int entityId, uint stackInstanceId, TargetSelection selection, HotkeySlot? activatedFromSlot = null)
    {
        pendingActions.Remove(entityId);
        pendingItemActivations.Merge(entityId, new PendingItemActivationComponent(stackInstanceId, selection, activatedFromSlot));
        movements.TryUpdate(entityId, new SetNextMapPositionUpdate(null));
    }

    /// <summary>Has entityId step to tile next, withdrawing any pending activation; null stands it still.</summary>
    public void Step(int entityId, Vector3Int? tile)
    {
        pendingActions.Remove(entityId);
        pendingItemActivations.Remove(entityId);
        movements.TryUpdate(entityId, new SetNextMapPositionUpdate(tile));
    }

    /// <summary>Has entityId step to tile because an activation it used moves it there (Dodge), leaving its pending requests alone.</summary>
    /// <remarks>Called from within the activation's own resolution, while the activation systems are still draining their pending pools.</remarks>
    public void StepFromActivation(int entityId, Vector3Int tile) =>
        movements.TryUpdate(entityId, new SetNextMapPositionUpdate(tile));
}
