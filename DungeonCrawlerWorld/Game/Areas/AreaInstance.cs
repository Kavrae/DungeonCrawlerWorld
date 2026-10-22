using Game.World;

namespace Game.Areas;

/// <summary>What one stamp of an area is, apart from the definition it came from or where it sits: for a fixed placement, the placement's Guid.</summary>
public readonly record struct AreaInstanceKey(Guid Value);

/// <summary>One stamp of an area: its identity, the definition it came from, the seed its residents and rolls are drawn from, and the world tile its top-left sits at.</summary>
/// <remarks>Generation and the Workshop stamp instances, never a definition directly, so an area that follows the player can be one instance stamped wherever it is entered.</remarks>
public sealed record AreaInstance(AreaInstanceKey Key, AreaDefinition Definition, ulong Seed, int OriginX, int OriginY)
{
    /// <summary>The instance a fixed placement stamps into a neighborhood generated from layoutSeed.</summary>
    public static AreaInstance ForFixedPlacement(AreaPlacement placement, AreaDefinition definition, int layoutSeed) => new(
        new AreaInstanceKey(placement.Id),
        definition,
        AreaSeeds.ForFixedPlacement(layoutSeed, placement.Id),
        Neighborhoods.OriginOf(placement.NeighborhoodX) + placement.OffsetX,
        Neighborhoods.OriginOf(placement.NeighborhoodY) + placement.OffsetY);

    /// <summary>Whether the world tile (x, y) is inside the instance's box.</summary>
    public bool Contains(int x, int y) =>
        x >= OriginX && y >= OriginY && x < OriginX + Definition.Width && y < OriginY + Definition.Height;
}
