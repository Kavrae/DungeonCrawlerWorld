using Engine.ECS.Components;

namespace Game.Modules.Auras.Components;

/// <summary>One aura an entity is standing inside, and when that aura next applies to it.</summary>
/// <remarks>
/// One instance per (entity, aura): an entity inside several auras holds one each, with independent
/// ticks. AuraSystem adds it when the entity comes into range and removes it on the first tick that
/// finds the entity out of range. Neither the power nor the effects are stored: every tick reads
/// the power fresh from the aura grid and the effects from the aura's definition. A keyed
/// timer-wheel timer (IKeyedScheduledTimer), keyed by AuraId.
/// </remarks>
public struct AuraExposureComponent(byte auraId, uint nextTickFrame) : IKeyedScheduledTimer
{
    private uint _timerWheelMark;

    public byte AuraId { get; set; } = auraId;

    /// <summary>Whether the aura's latest tick landed nothing because the entity refused it (an immunity). While set, the refusal is not reported again.</summary>
    public bool Refused { get; set; }

    /// <summary>The simulation frame of this aura's next tick (FrameDeadline).</summary>
    public uint NextTickFrame { get; set; } = nextTickFrame;

    readonly int IKeyedScheduledTimer.TimerKey => AuraId;

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"AuraId : {AuraId}\nRefused : {Refused}\nNextTickFrame : {NextTickFrame}";
}
