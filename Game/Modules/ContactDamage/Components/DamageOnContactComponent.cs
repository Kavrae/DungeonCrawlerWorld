namespace Game.Modules.ContactDamage.Components;

/// <summary>
/// Marks an entity (terrain, e.g. Lava, or in principle a creature) as dealing damage to
/// whatever steps onto it: DamagePerTick immediately on contact, then again every
/// TickIntervalFrames while the other entity remains. Generic on purpose -- not
/// lava-specific -- so a future hazard (spikes, acid) can reuse ContactDamageSystem with its
/// own numbers instead of a bespoke system.
/// </summary>
public struct DamageOnContactComponent(ushort damagePerTick, ushort tickIntervalFrames)
{
    public ushort DamagePerTick { get; set; } = damagePerTick;
    public ushort TickIntervalFrames { get; set; } = tickIntervalFrames;

    public override readonly string ToString() => $"DamagePerTick : {DamagePerTick}\nTickIntervalFrames : {TickIntervalFrames}";
}
