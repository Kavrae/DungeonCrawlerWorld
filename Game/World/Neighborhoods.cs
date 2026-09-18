using Engine.Math;

namespace Game.World;

/// <summary>The fixed 1024x1024-tile square the world is divided into -- the unit of processing tiers now, and of storage and streaming later.</summary>
/// <remarks>
/// A neighborhood spans every MapLayer. Tile-to-neighborhood conversion is an arithmetic shift, so
/// it floors: tile -1 is in neighborhood -1, not 0, which keeps negative world coordinates correct
/// without a floor-division helper at every call site.
/// </remarks>
public static class Neighborhoods
{
    public const int SizeShift = 10;

    public const int SizeTiles = 1 << SizeShift;

    /// <summary>The neighborhood coordinate containing tile coordinate value.</summary>
    public static int CellOf(int value) => value >> SizeShift;

    /// <summary>The tile coordinate of the neighborhood's first row or column.</summary>
    public static int OriginOf(int cell) => cell << SizeShift;

    /// <summary>The tiles of neighborhood (cellX, cellY), on depth layers.</summary>
    public static MapBounds AreaOf(int cellX, int cellY, int depth) => new(OriginOf(cellX), OriginOf(cellY), OriginOf(cellX + 1), OriginOf(cellY + 1), depth);

    /// <summary>Chebyshev distance in tiles from position to the nearest tile of neighborhood (cellX, cellY) -- 0 inside it. MapLayer is ignored.</summary>
    public static int DistanceToArea(Vector3Int position, int cellX, int cellY)
    {
        var dx = System.Math.Max(0, System.Math.Max(OriginOf(cellX) - position.X, position.X - (OriginOf(cellX + 1) - 1)));
        var dy = System.Math.Max(0, System.Math.Max(OriginOf(cellY) - position.Y, position.Y - (OriginOf(cellY + 1) - 1)));
        return System.Math.Max(dx, dy);
    }

    /// <summary>How many neighborhoods apart two positions are, as a Chebyshev distance over X/Y -- 0 for the same neighborhood, 1 for any of the 8 around it. MapLayer is ignored.</summary>
    public static int Distance(Vector3Int a, Vector3Int b) =>
        System.Math.Max(System.Math.Abs(CellOf(a.X) - CellOf(b.X)), System.Math.Abs(CellOf(a.Y) - CellOf(b.Y)));
}
