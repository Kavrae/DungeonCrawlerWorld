using Engine.ECS.Components;

namespace Game.Modules.Actions.Components;

/// <summary>One toggle an entity has on: its key, what it belongs to, and when its periodic effects next apply.</summary>
/// <remarks>
/// One instance per toggle that is on, so an entity with several holds several and each is ended on
/// its own. Key is what the toggle's held effects were granted under (EffectContext.HeldGrantKey), and
/// what takes them back. For an action this is the on/off state. For an item the lit state is the
/// stack's own (ToggleItemActivator.IsToggledOn), since it travels with the item; this is the holder's
/// side of each lit unit, so a stack of two lit units has two. Added and removed only by Toggles.
/// A keyed timer-wheel timer (IKeyedScheduledTimer) keyed by Key; FrameDeadline.Never for a toggle
/// with no periodic effects.
/// </remarks>
public struct ActiveToggleComponent(uint key, ActivatableReference owner, uint nextTickFrame) : IKeyedScheduledTimer
{
    /// <summary>Unique among the entity's active toggles, never 0, and fixed for as long as the toggle is on.</summary>
    public uint Key { get; } = key;

    /// <summary>What the toggle belongs to: an action, or the item stack holding the lit unit.</summary>
    public ActivatableReference Owner { get; } = owner;

    /// <summary>The simulation frame the toggle's periodic effects next apply on.</summary>
    public uint NextTickFrame { get; set; } = nextTickFrame;

    private uint _timerWheelMark;

    readonly int IKeyedScheduledTimer.TimerKey => unchecked((int)Key);

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"Key : {Key}\nOwner : {Owner}\nNextTickFrame : {NextTickFrame}";
}
