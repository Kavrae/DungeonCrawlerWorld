using Engine.ECS.Components;
using Game.Modules.StatusEffects;

namespace Game.Modules.StatusEffectAura.Components;

/// <summary>
/// Backs AuraSourceGrant's timed (DurationFrames-bearing) usage -- present on an entity only
/// while a timed aura source is still counting down, removed by AuraSourceExpirySystem (via
/// CountdownTicker) once it reaches 0, the same "no instance means inactive" convention
/// PotionCooldownComponent/TorchMarkComponent already use. Packed (at most one per entity), not
/// Multi: today only one StatusEffectType is ever granted with a duration at a time (Light, via
/// Scroll of Torch) -- a second simultaneously-timed type on the same entity would need this
/// promoted to a Multi pool (mirroring ScrollMasteryComponent's own (entityId, key)-keyed shape),
/// not supported yet since nothing needs it.
/// </summary>
public struct AuraSourceExpiryComponent(StatusEffectType type, int framesUntilNextTick) : ITickCountdown
{
    public StatusEffectType Type { get; set; } = type;
    public int FramesUntilNextTick { get; set; } = framesUntilNextTick;
}
