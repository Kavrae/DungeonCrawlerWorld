namespace Game.Areas;

/// <summary>The seeds an area instance and its residents are drawn from, mixed from stable values only -- Guids, ids and positions, never a hash that varies between runs.</summary>
public static class AreaSeeds
{
    private const ulong FnvOffsetBasis = 0xCBF29CE484222325;
    private const ulong FnvPrime = 0x100000001B3;

    /// <summary>Mixed into a symbol spawn's seed, so a cell's rolls never equal a placement's.</summary>
    private const ulong CellSalt = 0x43454C4C53504157;

    /// <summary>Mixed into a unique placement's seed, so it differs from the seed the same placement would give a generic one.</summary>
    private const ulong UniqueSalt = 0x554E49515545454E;

    /// <summary>A fixed placement's instance: the neighborhood's layout seed mixed with the placement's Guid, so the same place gives the same residents on every visit.</summary>
    public static ulong ForFixedPlacement(int layoutSeed, Guid placementId) => Mix((ulong)(uint)layoutSeed, Of(placementId));

    /// <summary>An explicit placement's rolls and seed within an instance.</summary>
    public static ulong ForPlacement(ulong instanceSeed, string placementId) => Mix(instanceSeed, Of(placementId));

    /// <summary>A symbol spawn's rolls and seed within an instance: its cell, its layer and its place in the cell's spawn list.</summary>
    public static ulong ForCell(ulong instanceSeed, int x, int y, int layer, int spawnIndex) =>
        Mix(Mix(instanceSeed ^ CellSalt, ((ulong)(uint)x << 32) | (uint)y), ((ulong)(uint)layer << 32) | (uint)spawnIndex);

    /// <summary>A unique placement's seed, from the area and the placement alone, so it is the same individual wherever it appears.</summary>
    public static uint ForUnique(Guid areaId, string placementId) => (uint)Mix(Of(areaId) ^ UniqueSalt, Of(placementId));

    public static ulong Of(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes);
        return Mix(BitConverter.ToUInt64(bytes[..8]), BitConverter.ToUInt64(bytes[8..]));
    }

    /// <summary>FNV-1a over the text's characters: stable across runs, unlike string.GetHashCode.</summary>
    public static ulong Of(string text)
    {
        var hash = FnvOffsetBasis;
        foreach (var character in text)
        {
            hash = (hash ^ character) * FnvPrime;
        }

        return hash;
    }

    /// <summary>SplitMix64's finalizer over the two values combined.</summary>
    public static ulong Mix(ulong first, ulong second)
    {
        var value = first + 0x9E3779B97F4A7C15 * (second + 1);
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EB;
        return value ^ (value >> 31);
    }
}
