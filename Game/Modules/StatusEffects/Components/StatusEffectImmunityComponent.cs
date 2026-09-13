using Engine.ECS.Components;
using Engine.ECS.Systems;

namespace Game.Modules.StatusEffects.Components;

/// <summary>
/// One active immunity to EffectType -- an entity holding N of these (MultiComponentPool) is
/// immune to N distinct StatusEffectTypes. Checked by each effect's own ApplyStack chokepoint
/// (PoisonEffects.ApplyStack, BurningEffects.ApplyStack, BurningAuraApplier's body-part-scoped
/// path) before a new stack ever gets added -- a hard on/off gate, not a StatModifierComponent
/// scale, since "immune" means the stack never lands at all, not "lands but does nothing".
/// </summary>
/// <remarks>
/// An expiring timer-wheel timer (IKeyedScheduledTimer), keyed by EffectType --
/// StatusEffectImmunityExpirySystem removes it on ExpiresAtFrame, and a permanent immunity
/// (FrameDeadline.Never) is never scheduled at all. The key is only unique while an entity holds
/// at most one instance per type, which is why every grant goes through
/// StatusEffectImmunityEffects.Grant: granting a type twice extends the one instance to the later
/// deadline instead of adding a second.
/// </remarks>
/// <param name="expiresAtFrame">The simulation frame the immunity ends on -- FrameDeadline.After(now, duration), or FrameDeadline.Never for permanent.</param>
public struct StatusEffectImmunityComponent(StatusEffectType effectType, uint expiresAtFrame) : IKeyedScheduledTimer
{
    private uint _timerWheelMark;

    public StatusEffectType EffectType { get; } = effectType;

    /// <summary>FrameDeadline.Never means "never expires", mirroring StatModifierComponent.ExpiresAtFrame.</summary>
    public uint ExpiresAtFrame { get; set; } = expiresAtFrame;

    uint IScheduledTimer.NextTickFrame { readonly get => ExpiresAtFrame; set => ExpiresAtFrame = value; }

    readonly int IKeyedScheduledTimer.TimerKey => (int)EffectType;

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() =>
        $"EffectType : {EffectType}\nExpiresAtFrame : {(ExpiresAtFrame == FrameDeadline.Never ? "Permanent" : ExpiresAtFrame.ToString())}";
}
