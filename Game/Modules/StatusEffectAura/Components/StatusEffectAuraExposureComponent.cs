using Engine.ECS.Components;
using Game.Modules.StatusEffects;

namespace Game.Modules.StatusEffectAura.Components;

/// <summary>
/// One instance per (entity, EffectType) currently within range of at least one
/// StatusEffectAuraSourceComponent of that type -- added/refreshed on entering range, removed
/// on leaving (see StatusEffectAuraSystem). Mirrors StatusEffectAuraSourceComponent's own
/// per-type-instance shape (a MultiComponentPool keyed by entity, EffectType as a field found
/// via a dense-chain walk) rather than one shared flag per entity: an entity can be in range of
/// several different effect types at once, each with its own independent tick timer, so a
/// newly-in-range type is never gated behind whether some OTHER type already has a running
/// exposure. StatusEffectAuraSystem always re-resolves "how many stacks" fresh from AuraGrid
/// rather than trusting a stale snapshot -- this component exists purely to drive each type's
/// own re-grant tick.
/// </summary>
/// <remarks>A keyed timer-wheel timer (IKeyedScheduledTimer), keyed by EffectType.</remarks>
public struct StatusEffectAuraExposureComponent(StatusEffectType effectType, uint nextTickFrame) : IKeyedScheduledTimer
{
    private uint _timerWheelMark;

    public StatusEffectType EffectType { get; set; } = effectType;

    /// <summary>The simulation frame of this type's next re-grant tick (FrameDeadline).</summary>
    public uint NextTickFrame { get; set; } = nextTickFrame;

    readonly int IKeyedScheduledTimer.TimerKey => (int)EffectType;

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"EffectType : {EffectType}\nNextTickFrame : {NextTickFrame}";
}
