using Engine.ECS.Components;

namespace Game.Modules.ContactDamage.Components;

/// <summary>
/// Present on an entity only while it currently stands on a DamageOnContactComponent tile --
/// added/refreshed on contact, removed when it steps off (see ContactDamageSystem). Only
/// caches SourceEntityId (the terrain entity that granted exposure) and the tick countdown --
/// DamagePerTick/TickIntervalFrames are looked up from the hazard itself (via SourceEntityId)
/// when needed rather than duplicated here, since terrain never moves and never changes once
/// placed, so there's nothing to protect against by copying its values out.
/// </summary>
public struct ContactDamageExposureComponent(ushort framesUntilNextTick, int sourceEntityId) : ITickCountdown
{
    public ushort FramesUntilNextTick { get; set; } = framesUntilNextTick;
    public int SourceEntityId { get; set; } = sourceEntityId;

    public override readonly string ToString() => $"FramesUntilNextTick : {FramesUntilNextTick}\nSourceEntityId : {SourceEntityId}";
}
