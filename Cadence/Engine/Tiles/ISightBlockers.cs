namespace Engine.Tiles;

/// <summary>Which tiles block sight: the walls line of sight is computed over (LineOfSight).</summary>
public interface ISightBlockers
{
    /// <summary>Changes whenever BlocksSight may answer differently for some tile, so anything worked out from these walls can be kept until it does.</summary>
    /// <remarks>Walls that never change keep it at 0.</remarks>
    int Version { get; }

    /// <summary>Whether the tile at (x, y) on map layer z blocks sight, filling its whole tile.</summary>
    bool BlocksSight(int x, int y, int z);
}
