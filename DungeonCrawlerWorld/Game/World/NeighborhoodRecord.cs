namespace Game.World;

/// <summary>What the world keeps about a neighborhood coordinate once it has been assigned, including after the neighborhood's contents are unloaded.</summary>
/// <remarks>
/// The layout (terrain and walls) is generated from Seed alone, so it comes back the same on every
/// visit. Each population draws a fresh seed mixed from Seed and how many times the neighborhood has
/// been populated, so a return brings new creatures while
/// a seeded session still generates the same ones, whatever else was generated before.
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
    public int PendingPopulationSeed => Mix(Seed, PopulationCount + 1);

    /// <summary>The seed for the next population, counting it as done.</summary>
    public int NextPopulationSeed() => Mix(Seed, ++PopulationCount);

    /// <summary>A 32-bit avalanche of seed and salt: deterministic across processes, unlike HashCode.Combine.</summary>
    private static int Mix(int seed, int salt)
    {
        unchecked
        {
            var hash = (uint)seed ^ ((uint)salt * 0x9E3779B9u);
            hash ^= hash >> 16;
            hash *= 0x85EBCA6Bu;
            hash ^= hash >> 13;
            hash *= 0xC2B2AE35u;
            hash ^= hash >> 16;
            return (int)hash;
        }
    }
}
