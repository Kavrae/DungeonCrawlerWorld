using Engine.ECS.Components;
using Game.Modules.StatusEffects;

namespace Game.Modules.StatusEffectAura.Components;

/// <summary>
/// Backs AuraSourceGrant's timed (DurationFrames-bearing) usage -- present on an entity only
/// while a timed aura source is still running, removed by AuraSourceExpirySystem on the frame it
/// expires, the same "no instance means inactive" convention PotionCooldownComponent/
/// TorchMarkComponent already use. Packed (at most one per entity), not Multi: today only one
/// StatusEffectType is ever granted with a duration at a time (Light, via Scroll of Torch) -- a
/// second simultaneously-timed type on the same entity would need this promoted to a Multi pool
/// (a keyed timer, mirroring ScrollMasteryComponent's own (entityId, key)-keyed shape), not
/// supported yet since nothing needs it.
/// </summary>
/// <remarks>A timer-wheel timer (IScheduledTimer) whose one firing is the expiry.</remarks>
/// <param name="expiresAtFrame">The simulation frame the aura source is revoked on.</param>
public struct AuraSourceExpiryComponent(StatusEffectType type, uint expiresAtFrame) : IScheduledTimer
{
    private uint _timerWheelMark;

    public StatusEffectType Type { get; set; } = type;

    /// <summary>The simulation frame the aura source is revoked on.</summary>
    public uint ExpiresAtFrame { get; set; } = expiresAtFrame;

    uint IScheduledTimer.NextTickFrame { readonly get => ExpiresAtFrame; set => ExpiresAtFrame = value; }

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"Type : {Type}\nExpiresAtFrame : {ExpiresAtFrame}";
}
