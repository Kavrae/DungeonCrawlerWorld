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
/// </remarks>
public struct BodyPartBurningTimerComponent(byte partId, byte stackCount, ushort framesUntilNextTick, StatusEffectSource source) : ITickCountdown
{
    /// <summary>The specific BodyPartComponent.PartId this timer is burning, re-located each tick via BodyPartSelection.FindByPartId rather than a dense index (which isn't a stable identity).</summary>
    public byte PartId { get; set; } = partId;

    public byte StackCount { get; set; } = stackCount;

    public ushort FramesUntilNextTick { get; set; } = framesUntilNextTick;

    /// <summary>Set once on the 0-to-1 transition (BurningAuraApplier.ApplyBodyPartScopedStack), never overwritten by a later top-off -- mirrors BurningTimerComponent's own Source field.</summary>
    public StatusEffectSource Source { get; set; } = source;

    public override readonly string ToString() => $"PartId : {PartId}\nFramesUntilNextTick : {FramesUntilNextTick}\nStackCount : {StackCount}\nSource : {Source}";
}
