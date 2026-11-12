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
    private const int NoOccupant = -1;

    private static readonly int TerrainLayerCount = Enum.GetValues<TerrainLayer>().Length;

    public MapNeighborhood(int width, int height, int depth, int layoutSeed)
    {
        Width = width;
        Height = height;
        LayoutSeed = layoutSeed;
        PlaneSize = width * height;

        var volume = PlaneSize * depth;

        BlockingEntityIds = new int[volume];
        Array.Fill(BlockingEntityIds, -1);

        OccupantSlotByCell = new int[volume];
        Array.Fill(OccupantSlotByCell, NoOccupant);

        OccupiedLayerMaskByColumn = new byte[PlaneSize];

        TerrainTypeIds = new ushort[PlaneSize * TerrainLayerCount];

        StructureTypeIds = new ushort[volume];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The seed its layout was generated from, which every cell's look is picked from (CellSeeds); 0 for a neighborhood loaded empty.</summary>
    public int LayoutSeed { get; set; }

    /// <summary>Width * Height: the offset of one MapLayer's cells from the next.</summary>
    public int PlaneSize { get; }

    public int[] BlockingEntityIds { get; }

    /// <summary>Per cell: -1 when it holds nobody, its one occupant's entity id, or below -1 an index into OccupantLists (Map.OccupantListIndexOf) when it holds several -- see Map's remarks on the occupant index.</summary>
    public int[] OccupantSlotByCell { get; }

    public List<List<int>> OccupantLists { get; } = [];

    /// <summary>Slots in OccupantLists whose cell is back to one occupant or none, ready for the next cell that gains a second.</summary>
    public Stack<int> FreeOccupantListIndices { get; } = new();

    public byte[] OccupiedLayerMaskByColumn { get; }

    public ushort[] TerrainTypeIds { get; }

    public ushort[] StructureTypeIds { get; }
}
