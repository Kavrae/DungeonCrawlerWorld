using Game.Modules.Health.Components;

namespace Game.Modules.ContactDamage.Components;

/// <summary>
/// Marks an entity (terrain, e.g. Lava, or in principle a creature) as dealing damage to
/// whatever steps onto it: DamagePerTick immediately on contact, then again every
/// TickIntervalFrames while the other entity remains. Generic on purpose -- not
/// lava-specific -- so a future hazard (spikes, acid) can reuse ContactDamageSystem with its
/// own numbers instead of a bespoke system.
/// </summary>
/// <remarks>PreferredTargetType is optional -- ContactDamageSystem hardcodes a Bottommost fallback for every hazard that sets it, so this only ever needs to name the preferred type, not its own fallback strategy.</remarks>
public struct DamageOnContactComponent(ushort damagePerTick, ushort tickIntervalFrames, BodyPartType? preferredTargetType = null)
{
    public ushort DamagePerTick { get; set; } = damagePerTick;
    public ushort TickIntervalFrames { get; set; } = tickIntervalFrames;
    public BodyPartType? PreferredTargetType { get; set; } = preferredTargetType;

    public override readonly string ToString() => $"DamagePerTick : {DamagePerTick}\nTickIntervalFrames : {TickIntervalFrames}";
}
