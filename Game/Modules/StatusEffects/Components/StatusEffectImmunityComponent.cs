using Engine.ECS.Components;
using Engine.ECS.Systems;

namespace Game.Modules.StatusEffects.Components;

/// <summary>
/// One active immunity to EffectType -- an entity holding N of these (MultiComponentPool) is
/// immune to N distinct StatusEffectTypes. Checked by each effect's own ApplyStack chokepoint
/// (PoisonEffects.ApplyStack, BurningEffects.ApplyStack, BurningApplier's body-part-scoped
/// path) before a new stack ever gets added -- a hard on/off gate, not a StatModifierComponent
/// scale, since "immune" means the stack never lands at all, not "lands but does nothing".
/// </summary>
/// <remarks>
/// An expiring timer-wheel timer (IKeyedScheduledTimer), keyed by EffectType --
/// StatusEffectImmunityExpirySystem removes it on ExpiresAtFrame, and a permanent immunity
/// (FrameDeadline.Never) is never scheduled at all. The key is only unique while an entity holds
/// at most one unheld instance per type, which is why every grant goes through
/// StatusEffectImmunityEffects.Grant: granting a type twice extends the one instance to the later
/// deadline instead of adding a second. A toggle holding an immunity adds an instance of its own under
/// its key (StatusEffectImmunityEffects.GrantHeld), beside any other, and takes back only that one.
/// </remarks>
/// <param name="expiresAtFrame">The simulation frame the immunity ends on -- FrameDeadline.After(now, duration), or FrameDeadline.Never for permanent.</param>
/// <param name="heldGrantKey">The key of the toggle holding this immunity (EffectContext.HeldGrantKey), or 0 for one no toggle holds. A held immunity never expires and is ended only by its toggle.</param>
public struct StatusEffectImmunityComponent(StatusEffectType effectType, uint expiresAtFrame, uint heldGrantKey = StatusEffectImmunityComponent.NoHeldGrantKey) : IKeyedScheduledTimer
{
    /// <summary>HeldGrantKey of an immunity no toggle holds.</summary>
    public const uint NoHeldGrantKey = 0;

    private uint _timerWheelMark;

    public StatusEffectType EffectType { get; } = effectType;

    /// <summary>The key of the toggle holding this immunity, or NoHeldGrantKey.</summary>
    public uint HeldGrantKey { get; } = heldGrantKey;

    /// <summary>FrameDeadline.Never means "never expires", mirroring StatModifierComponent.ExpiresAtFrame.</summary>
    public uint ExpiresAtFrame { get; set; } = expiresAtFrame;

    uint IScheduledTimer.NextTickFrame { readonly get => ExpiresAtFrame; set => ExpiresAtFrame = value; }

    /// <summary>The type for the one unheld instance per type; a distinct negative key for each held one, which is never scheduled (it never expires) but must never be mistaken for the unheld one.</summary>
    readonly int IKeyedScheduledTimer.TimerKey => HeldGrantKey == NoHeldGrantKey
        ? (int)EffectType
        : -1 - (int)((HeldGrantKey << 5) | (uint)EffectType);

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() =>
        $"EffectType : {EffectType}\nExpiresAtFrame : {(ExpiresAtFrame == FrameDeadline.Never ? "Permanent" : ExpiresAtFrame.ToString())}";
}
