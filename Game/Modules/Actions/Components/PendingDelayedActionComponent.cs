using Engine.ECS.Components;
using Engine.Math;

namespace Game.Modules.Actions.Components;

/// <summary>
/// Written when a Delayed-category action activates (after the shared ActionLock windup is
/// set) and cleared once DelayedActionSystem resolves its effect, or immediately by a
/// cancellation (right-click tap / Escape). At most one per entity -- a second Delayed
/// activation can't happen while the shared ActionLock still blocks the entity, so this never
/// needs to hold more than one pending action.
/// </summary>
/// <remarks>
/// A timer-wheel timer (IScheduledTimer) whose one firing resolves the action. ReadyAtFrame is
/// copied from the shared ActionLockComponent's own UnlockedAtFrame when the action is queued, so
/// the windup and the resolution read the same deadline and cannot drift apart -- the invariant
/// DelayedActionSystem used to defend by sharing a tier cadence with ActionLockSystem, and the one
/// MapWindow's charge-fill telegraph depends on (see PLAN-charge-attack-fill-indicator.md).
/// </remarks>
/// <param name="readyAtFrame">The frame the windup ends and the effect resolves -- the lock's own UnlockedAtFrame.</param>
public struct PendingDelayedActionComponent(Guid actionId, Vector3Int[] targetTiles, uint readyAtFrame) : IScheduledTimer
{
    private uint _timerWheelMark;

    public Guid ActionId { get; set; } = actionId;
    public Vector3Int[] TargetTiles { get; set; } = targetTiles;

    /// <summary>The frame this windup completes on.</summary>
    public uint ReadyAtFrame { get; set; } = readyAtFrame;

    uint IScheduledTimer.NextTickFrame { readonly get => ReadyAtFrame; set => ReadyAtFrame = value; }

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"ActionId : {ActionId}\nReadyAtFrame : {ReadyAtFrame}\nTargetTiles : [{string.Join(", ", TargetTiles)}]";
}
