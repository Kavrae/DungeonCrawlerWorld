namespace Game.World;

/// <summary>What the world keeps about a neighborhood coordinate once it has been assigned, including after the neighborhood's contents are unloaded.</summary>
/// <remarks>
/// The layout (terrain and walls) is generated from Seed alone, so it comes back the same on every visit. Each
/// population draws a fresh seed derived from Seed and how many times the neighborhood has been populated
/// (NeighborhoodSeeds.ForPopulation), so a return brings new creatures while a seeded session that populates a
/// neighborhood as many times generates the same ones, whatever else happened in between.
/// </remarks>
public sealed class NeighborhoodRecord(int cellX, int cellY, int seed)
{
    public int CellX { get; } = cellX;

    public int CellY { get; } = cellY;

    /// <summary>The seed the layout is generated from.</summary>
    public int Seed { get; } = seed;

    /// <summary>How many times this neighborhood has been populated.</summary>
    public int PopulationCount { get; private set; }

    /// <summary>The seed the next population will draw, without counting it -- for planning a population that may yet be cancelled.</summary>
    public int PendingPopulationSeed => NeighborhoodSeeds.ForPopulation(Seed, PopulationCount + 1);

    /// <summary>The seed for the next population, counting it as done.</summary>
    public int NextPopulationSeed() => NeighborhoodSeeds.ForPopulation(Seed, ++PopulationCount);
}
