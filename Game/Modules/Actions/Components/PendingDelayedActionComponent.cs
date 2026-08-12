using Engine.Math;

namespace Game.Modules.Actions.Components;

/// <summary>
/// Written when a Delayed-category action activates (after the shared ActionLock windup is
/// set) and cleared once DelayedActionSystem resolves its effect, or immediately by a
/// cancellation (right-click tap / Escape). At most one per entity -- a second Delayed
/// activation can't happen while the shared ActionLock still blocks the entity, so this never
/// needs to hold more than one pending action.
/// </summary>
public struct PendingDelayedActionComponent(Guid actionId, Vector3Int[] targetTiles)
{
    public Guid ActionId { get; set; } = actionId;
    public Vector3Int[] TargetTiles { get; set; } = targetTiles;

    public override readonly string ToString() => $"ActionId : {ActionId}\nTargetTiles : [{string.Join(", ", TargetTiles)}]";
}
