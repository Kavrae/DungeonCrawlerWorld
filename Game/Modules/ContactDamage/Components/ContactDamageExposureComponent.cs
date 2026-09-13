using Engine.ECS.Components;

namespace Game.Modules.ContactDamage.Components;

/// <summary>
/// Present on an entity only while it currently stands on a DamageOnContactComponent tile --
/// added/refreshed on contact, removed when it steps off (see ContactDamageSystem). Only
/// caches SourceEntityId (the terrain entity that granted exposure) and the next tick's frame --
/// DamagePerTick/TickIntervalFrames are looked up from the hazard itself (via SourceEntityId)
/// when needed rather than duplicated here, since terrain never moves and never changes once
/// placed, so there's nothing to protect against by copying its values out.
/// </summary>
/// <remarks>A timer-wheel timer (IScheduledTimer): writing NextTickFrame is all it takes to schedule it.</remarks>
public struct ContactDamageExposureComponent(uint nextTickFrame, int sourceEntityId) : IScheduledTimer
{
    private uint _timerWheelMark;

    /// <summary>The simulation frame of the next contact-damage tick (FrameDeadline).</summary>
    public uint NextTickFrame { get; set; } = nextTickFrame;

    public int SourceEntityId { get; set; } = sourceEntityId;

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"NextTickFrame : {NextTickFrame}\nSourceEntityId : {SourceEntityId}";
}
