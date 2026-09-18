using Game.Modules.Core.Components;

namespace Game.World;

/// <summary>One neighborhood's slice of every per-cell store Map keeps -- the unit the sliding window loads and unloads.</summary>
/// <remarks>
/// Cells are addressed by their offset within the neighborhood, X fastest. A neighborhood cut off by
/// the map's edge is only as wide and tall as the part inside the map, so a small map allocates only
/// what it has rather than a full 1024x1024. Map owns all access; nothing else holds one of these.
/// </remarks>
internal sealed class MapNeighborhood
{
    private const int NoOccupantListIndex = -1;

    private static readonly int TerrainLayerCount = Enum.GetValues<TerrainLayer>().Length;

    public MapNeighborhood(int width, int height, int depth)
    {
        Width = width;
        Height = height;
        PlaneSize = width * height;

        var volume = PlaneSize * depth;

        BlockingEntityIds = new int[volume];
        Array.Fill(BlockingEntityIds, -1);

        OccupantListIndexByCell = new int[volume];
        Array.Fill(OccupantListIndexByCell, NoOccupantListIndex);

        OccupiedLayerMaskByColumn = new byte[PlaneSize];

        TerrainTypeIds = new ushort[PlaneSize * TerrainLayerCount];
        TerrainVariants = new byte[PlaneSize * TerrainLayerCount];

        StructureTypeIds = new ushort[volume];
        StructureVariants = new byte[volume];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Width * Height: the offset of one MapLayer's cells from the next.</summary>
    public int PlaneSize { get; }

    public int[] BlockingEntityIds { get; }

    /// <summary>Per-cell index into OccupantLists, or -1 when the cell holds nobody -- see Map's remarks on the occupant index.</summary>
    public int[] OccupantListIndexByCell { get; }

    public List<List<int>> OccupantLists { get; } = [];

    /// <summary>Slots in OccupantLists whose cell has emptied, ready for the next cell that gains an occupant.</summary>
    public Stack<int> FreeOccupantListIndices { get; } = new();

    public byte[] OccupiedLayerMaskByColumn { get; }

    public ushort[] TerrainTypeIds { get; }

    public byte[] TerrainVariants { get; }

    public ushort[] StructureTypeIds { get; }

    public byte[] StructureVariants { get; }
}
