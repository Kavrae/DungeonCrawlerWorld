using Engine.ECS.Components;

namespace Game.Modules.Actions.Components;

/// <summary>
/// One windup in progress: written when a Delayed action or item activates (after the shared
/// ActionLock windup is set) and cleared once DelayedActionSystem resolves it, or immediately by a
/// cancellation (right-click tap / Escape, a Stagger, Dodge, a freeze). At most one per entity -- a
/// second Delayed activation can't happen while the shared ActionLock still blocks the entity, so
/// this never needs to hold more than one.
/// </summary>
/// <remarks>
/// <para>
/// One component for actions and items, so every rule about a windup -- one per entity, every way
/// one is cancelled, the charge-fill telegraph -- covers both with no second copy. What it resolves
/// into is Activatable: an action resolves in Actions, an item through the resolver Inventory registers
/// (WindupResolvers).
/// </para>
/// <para>
/// A timer-wheel timer (IScheduledTimer) whose one firing resolves the windup. ReadyAtFrame is
/// copied from the shared ActionLockComponent's own UnlockedAtFrame when the windup starts
/// (Windups.Begin), so the windup and the resolution read the same deadline and cannot drift apart
/// -- the invariant MapWindow's charge-fill telegraph depends on.
/// </para>
/// </remarks>
/// <param name="activatable">What the windup resolves into: the action, or the item stack.</param>
/// <param name="selection">What the caster aimed at.</param>
/// <param name="readyAtFrame">The frame the windup ends and resolves -- the lock's own UnlockedAtFrame.</param>
public struct PendingWindupComponent(ActivatableReference activatable, TargetSelection selection, uint readyAtFrame) : IScheduledTimer
{
    /// <summary>What the windup resolves into.</summary>
    public ActivatableReference Activatable { get; set; } = activatable;

    /// <summary>What the caster aimed at, resolved into tiles when the windup ends (TargetResolution), so a Target-mode windup lands wherever its target is then.</summary>
    public TargetSelection Selection { get; set; } = selection;

    /// <summary>The frame this windup completes on.</summary>
    public uint ReadyAtFrame { get; set; } = readyAtFrame;

    /// <summary>The hotkey slot an item's activation came from, or null; the slot that follows the unit if resolving moves it to another stack.</summary>
    public HotkeySlot? ActivatedFromSlot { get; set; }

    private uint _timerWheelMark;

    /// <summary>A windup that resolves into the action actionId.</summary>
    public static PendingWindupComponent ForAction(Guid actionId, TargetSelection selection, uint readyAtFrame) =>
        new(ActivatableReference.Action(actionId), selection, readyAtFrame);

    /// <summary>A windup that resolves into activating the stack stackInstanceId.</summary>
    public static PendingWindupComponent ForItem(uint stackInstanceId, HotkeySlot? activatedFromSlot, TargetSelection selection, uint readyAtFrame) =>
        new(ActivatableReference.ItemStack(stackInstanceId), selection, readyAtFrame) { ActivatedFromSlot = activatedFromSlot };

    uint IScheduledTimer.NextTickFrame { readonly get => ReadyAtFrame; set => ReadyAtFrame = value; }

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"Activatable : {Activatable}\nReadyAtFrame : {ReadyAtFrame}\nSelection : {Selection}";
}
