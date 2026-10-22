using Engine.Math;

namespace Game.Areas;

/// <summary>A generic, repeated resident a legend symbol spawns: one blueprint, or one of several by weight, each rolled from the area instance's seed.</summary>
/// <param name="Blueprint">The blueprint spawned; null when OneOf chooses.</param>
/// <param name="Size">The footprint; null for the blueprint's own.</param>
/// <param name="Chance">The percent chance it spawns at all, 1 to 100.</param>
/// <param name="OneOf">The blueprints one is chosen from by weight; empty when Blueprint names it.</param>
public sealed record AreaSpawn(
    Guid? Blueprint = null,
    Vector2Byte? Size = null,
    byte Chance = AreaSpawn.AlwaysChance,
    IReadOnlyList<AreaWeightedBlueprint>? OneOf = null)
{
    public const byte AlwaysChance = 100;

    public IReadOnlyList<AreaWeightedBlueprint> OneOfList => OneOf ?? [];
}

/// <summary>One choice of an AreaSpawn's OneOf: a blueprint and how likely it is against the others.</summary>
public sealed record AreaWeightedBlueprint(Guid Blueprint, int Weight = AreaWeightedBlueprint.DefaultWeight)
{
    public const int DefaultWeight = 1;
}
