using Game.Modules.Core.Components;

namespace Game.World;

/// <summary>The seed a map cell's look is picked from: a mix of its neighborhood's layout seed, its position and which store it is in.</summary>
/// <remarks>
/// Nothing is stored per cell and nothing is drawn from a random sequence, so the same tile looks the same on every visit, after
/// any edit, and however many pictures its terrain has: whatever draws it reduces the seed by its own variant count. Terrain on each
/// TerrainLayer and structures on each MapLayer get their own salt, so a floor and the wall on it don't pick in step; a wall's
/// face and top read the same seed, so matching chances pick matching sets. Deterministic across processes, unlike HashCode.
/// </remarks>
public static class CellSeeds
{
    private const int TerrainStoreSalt = 0x7465;
    private const int StructureStoreSalt = 0x7374;

    public static uint ForTerrain(int layoutSeed, int x, int y, TerrainLayer terrainLayer) =>
        Mix(layoutSeed, x, y, TerrainStoreSalt + (int)terrainLayer);

    public static uint ForStructure(int layoutSeed, int x, int y, int mapLayer) =>
        Mix(layoutSeed, x, y, StructureStoreSalt + mapLayer);

    private static uint Mix(int layoutSeed, int x, int y, int store)
    {
        unchecked
        {
            var hash = Avalanche((uint)layoutSeed ^ ((uint)x * 0x9E3779B1u));
            hash = Avalanche(hash ^ ((uint)y * 0x85EBCA6Bu));
            return Avalanche(hash ^ ((uint)store * 0xC2B2AE35u));
        }
    }

    private static uint Avalanche(uint hash)
    {
        unchecked
        {
            hash ^= hash >> 16;
            hash *= 0x85EBCA6Bu;
            hash ^= hash >> 13;
            hash *= 0xC2B2AE35u;
            hash ^= hash >> 16;
            return hash;
        }
    }
}
