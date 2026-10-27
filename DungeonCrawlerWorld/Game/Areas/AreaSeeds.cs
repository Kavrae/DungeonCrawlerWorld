using Engine.Math;

namespace Game.Areas;

/// <summary>The seeds an area instance and its residents are drawn from, mixed from stable values only -- Guids, ids and positions, never a hash that varies between runs.</summary>
public static class AreaSeeds
{
    /// <summary>Mixed into a symbol spawn's seed, so a cell's rolls never equal a placement's.</summary>
    private const ulong CellSalt = 0x43454C4C53504157;

    /// <summary>Mixed into a unique placement's seed, so it differs from the seed the same placement would give a generic one.</summary>
    private const ulong UniqueSalt = 0x554E49515545454E;

    /// <summary>A fixed placement's instance: the neighborhood's layout seed mixed with the placement's Guid, so the same place gives the same residents on every visit.</summary>
    public static ulong ForFixedPlacement(int layoutSeed, Guid placementId) => SeedMix.Mix(SeedMix.Of(layoutSeed), SeedMix.Of(placementId));

    /// <summary>An explicit placement's rolls and seed within an instance.</summary>
    public static ulong ForPlacement(ulong instanceSeed, string placementId) => SeedMix.Mix(instanceSeed, SeedMix.Of(placementId));

    /// <summary>A symbol spawn's rolls and seed within an instance: its cell, its layer and its place in the cell's spawn list.</summary>
    public static ulong ForCell(ulong instanceSeed, int x, int y, int layer, int spawnIndex) =>
        SeedMix.Mix(SeedMix.Mix(instanceSeed ^ CellSalt, SeedMix.Pack(x, y)), SeedMix.Pack(layer, spawnIndex));

    /// <summary>A unique placement's seed, from the area and the placement alone, so it is the same individual wherever it appears.</summary>
    public static uint ForUnique(Guid areaId, string placementId) => (uint)SeedMix.Mix(SeedMix.Of(areaId) ^ UniqueSalt, SeedMix.Of(placementId));
}
