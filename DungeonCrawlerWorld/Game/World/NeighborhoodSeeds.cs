using Engine.Math;

namespace Game.World;

/// <summary>The seeds a neighborhood is generated from, derived from the session seed and where it is -- never drawn from a sequence.</summary>
/// <remarks>
/// A neighborhood's layout seed is the same whatever the player did before reaching it and whatever order coordinates
/// were first needed in, so a seeded session generates the same world however it is played. Only its population seed
/// moves with play, and only by how many times it has been populated (see NeighborhoodRecord).
/// </remarks>
public static class NeighborhoodSeeds
{
    /// <summary>Mixed into the session seed for layout seeds, so one never equals another sequence seeded from the same session seed.</summary>
    private const ulong LayoutSalt = 0x4E45494748424F52;

    /// <summary>Mixed into a layout seed for its population seeds, so a population seed never equals a layout seed.</summary>
    private const ulong PopulationSalt = 0x504F50554C415445;

    /// <summary>The layout seed of neighborhood (cellX, cellY) on floorNumber in a session seeded with worldSeed.</summary>
    public static int ForLayout(int worldSeed, int floorNumber, int cellX, int cellY) =>
        (int)SeedMix.Mix(SeedMix.Mix(SeedMix.Of(worldSeed) ^ LayoutSalt, SeedMix.Of(floorNumber)), SeedMix.Pack(cellX, cellY));

    /// <summary>The seed of a neighborhood's visitIndex-th population (the first is 1).</summary>
    public static int ForPopulation(int layoutSeed, int visitIndex) =>
        (int)SeedMix.Mix(SeedMix.Of(layoutSeed) ^ PopulationSalt, SeedMix.Of(visitIndex));
}
