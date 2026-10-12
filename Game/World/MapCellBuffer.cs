namespace Game.World;

/// <summary>A rectangle of map cells copied out in one read: per cell its terrain and structure type ids, the seeds their looks are picked from, and whether it is on the map.</summary>
/// <remarks>
/// Filled by Map.CopyCells (through IMapViewQuery.CopyCells) for whoever draws a stretch of the map at once -- the map's terrain
/// image, the map editor's view of an authored layout -- so they read arrays instead of asking cell by cell. The caller owns
/// and reuses it: it grows to the largest area asked for and never shrinks, so a steady view allocates nothing. Row-major, X
/// fastest; a cell outside the copied rectangle reads as off the map.
/// </remarks>
public sealed class MapCellBuffer
{
    private ushort[] _terrainTypeIds = [];
    private ushort[] _structureTypeIds = [];
    private uint[] _terrainSeeds = [];
    private uint[] _structureSeeds = [];
    private bool[] _isOnMap = [];

    /// <summary>The copied rectangle's first column.</summary>
    public int MinX { get; private set; }

    /// <summary>The copied rectangle's first row.</summary>
    public int MinY { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>The MapLayer the rectangle was copied from.</summary>
    public int MapLayer { get; private set; }

    /// <summary>Whether (x, y) is inside the copied rectangle and on the map.</summary>
    public bool IsOnMap(int x, int y) => TryIndexOf(x, y, out var index) && _isOnMap[index];

    /// <summary>The terrain type id under the layer at (x, y); 0 off the map, outside the rectangle, or on a layer with no floor.</summary>
    public ushort TerrainTypeIdAt(int x, int y) => TryIndexOf(x, y, out var index) ? _terrainTypeIds[index] : (ushort)0;

    /// <summary>The structure type id on the layer at (x, y); 0 where there is none, off the map or outside the rectangle.</summary>
    public ushort StructureTypeIdAt(int x, int y) => TryIndexOf(x, y, out var index) ? _structureTypeIds[index] : (ushort)0;

    /// <summary>The seed the look of the terrain at (x, y) is picked from (CellSeeds).</summary>
    public uint TerrainSeedAt(int x, int y) => TryIndexOf(x, y, out var index) ? _terrainSeeds[index] : 0;

    /// <summary>The seed the look of the structure at (x, y) is picked from (CellSeeds), its face and top alike.</summary>
    public uint StructureSeedAt(int x, int y) => TryIndexOf(x, y, out var index) ? _structureSeeds[index] : 0;

    /// <summary>Sizes the buffer for a rectangle and clears it to off the map, for a writer to fill.</summary>
    public void Reset(int minX, int minY, int width, int height, int mapLayer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        MinX = minX;
        MinY = minY;
        Width = width;
        Height = height;
        MapLayer = mapLayer;

        var count = width * height;
        if (_isOnMap.Length < count)
        {
            _terrainTypeIds = new ushort[count];
            _structureTypeIds = new ushort[count];
            _terrainSeeds = new uint[count];
            _structureSeeds = new uint[count];
            _isOnMap = new bool[count];
            return;
        }

        Array.Clear(_terrainTypeIds, 0, count);
        Array.Clear(_structureTypeIds, 0, count);
        Array.Clear(_terrainSeeds, 0, count);
        Array.Clear(_structureSeeds, 0, count);
        Array.Clear(_isOnMap, 0, count);
    }

    /// <summary>Writes one on-map cell; index is its row-major place in the rectangle.</summary>
    public void Write(int index, ushort terrainTypeId, uint terrainSeed, ushort structureTypeId, uint structureSeed)
    {
        _terrainTypeIds[index] = terrainTypeId;
        _terrainSeeds[index] = terrainSeed;
        _structureTypeIds[index] = structureTypeId;
        _structureSeeds[index] = structureSeed;
        _isOnMap[index] = true;
    }

    private bool TryIndexOf(int x, int y, out int index)
    {
        var column = x - MinX;
        var row = y - MinY;
        if ((uint)column < (uint)Width && (uint)row < (uint)Height)
        {
            index = column + row * Width;
            return true;
        }

        index = -1;
        return false;
    }
}
