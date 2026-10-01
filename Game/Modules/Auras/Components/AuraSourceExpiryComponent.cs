using Engine.ECS.Components;

namespace Game.Modules.Auras.Components;

/// <summary>
/// Backs AuraSourceGrant's timed (DurationFrames-bearing) usage -- present on an entity only
/// while a timed aura source is still running, removed by AuraSourceExpirySystem on the frame it
/// expires, the same "no instance means inactive" convention PotionCooldownComponent/
/// TorchMarkComponent already use. Packed (at most one per entity), not Multi: today only one
/// aura is ever granted with a duration at a time (Light, via Scroll of Torch) -- a
/// second simultaneously-timed aura on the same entity would need this promoted to a Multi pool
/// (a keyed timer, mirroring ScrollMasteryComponent's own (entityId, key)-keyed shape), not
/// supported yet since nothing needs it.
/// </summary>
/// <remarks>A timer-wheel timer (IScheduledTimer) whose one firing is the expiry.</remarks>
/// <param name="auraId">The session-local id (AuraCatalog) of the aura whose source expires.</param>
/// <param name="expiresAtFrame">The simulation frame the aura source is revoked on.</param>
public struct AuraSourceExpiryComponent(byte auraId, uint expiresAtFrame) : IScheduledTimer
{
    private uint _timerWheelMark;

    public byte AuraId { get; set; } = auraId;

    /// <summary>The simulation frame the aura source is revoked on.</summary>
    public uint ExpiresAtFrame { get; set; } = expiresAtFrame;

    uint IScheduledTimer.NextTickFrame { readonly get => ExpiresAtFrame; set => ExpiresAtFrame = value; }

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"AuraId : {AuraId}\nExpiresAtFrame : {ExpiresAtFrame}";
}
