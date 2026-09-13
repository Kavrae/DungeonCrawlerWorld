using Engine.ECS.Components;
using Game.World;

namespace Game.Modules.Health.Components;

/// <summary>Present on a body part only while that specific part currently has at least one body-part-scoped Burning stack -- one instance per currently-burning part, in a MultiComponentPool keyed by entityId (see BodyPartBurningSystem).</summary>
/// <remarks>
/// Lives under Game.Modules.Health rather than Game.Modules.Burning (unlike BurningTimerComponent,
/// its entity-scoped counterpart) because BodyPartSelection.PickLowestPercentage (Health module)
/// needs to read this pool directly to exclude a burning part from regen -- Health never otherwise
/// depends on an effect-specific component type, so keeping that direction one-way (Burning depends
/// on Health, never the reverse) means the component itself has to sit on the Health side.
///
/// A keyed timer-wheel timer (IKeyedScheduledTimer): PartId names the instance, since an entity can
/// have several parts burning at once.
/// </remarks>
public struct BodyPartBurningTimerComponent(byte partId, byte stackCount, uint nextTickFrame, StatusEffectSource source) : IKeyedScheduledTimer
{
    private uint _timerWheelMark;

    /// <summary>The specific BodyPartComponent.PartId this timer is burning, re-located each tick via BodyPartSelection.FindByPartId rather than a dense index (which isn't a stable identity). Also this timer's TimerKey.</summary>
    public byte PartId { get; set; } = partId;

    public byte StackCount { get; set; } = stackCount;

    /// <summary>The simulation frame of the next damage tick (FrameDeadline).</summary>
    public uint NextTickFrame { get; set; } = nextTickFrame;

    /// <summary>Set once on the 0-to-1 transition (BurningAuraApplier.ApplyBodyPartScopedStack), never overwritten by a later top-off -- mirrors BurningTimerComponent's own Source field.</summary>
    public StatusEffectSource Source { get; set; } = source;

    readonly int IKeyedScheduledTimer.TimerKey => PartId;

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"PartId : {PartId}\nNextTickFrame : {NextTickFrame}\nStackCount : {StackCount}\nSource : {Source}";
}
