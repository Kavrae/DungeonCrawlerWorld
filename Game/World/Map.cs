using Engine.Math;
using Game.Modules.Core.Components;

namespace Game.World;

/// <summary>
/// The in-memory map grid. Two independent flat stores: creature occupancy (one Blocking
/// entity per (x,y,MapLayer) cell -- see World.IsBlocking for why Tiny/Phasing entities never
/// occupy this) and terrain (the floor beneath UnderGround/Ground -- Flying has none). Kept
/// separate so a wall and the floor it stands on don't compete for the same slot.
/// </summary>
public sealed class Map
{
    private const int TerrainLayerCount = 2;

    public Vector3Int Size { get; }

    private readonly int[] _occupantEntityIds;
    private readonly int[] _terrainEntityIds;

    public Map(Vector3Int size)
    {
        Size = size;

        _occupantEntityIds = new int[size.Volume];
        Array.Fill(_occupantEntityIds, -1);

        _terrainEntityIds = new int[size.X * size.Y * TerrainLayerCount];
        Array.Fill(_terrainEntityIds, -1);
    }

    /// <summary>(0,0,0) is drawn to the top-left of the map window.</summary>
    public int GetEntityId(Vector3Int coordinates) => _occupantEntityIds[coordinates.FlatIndex(Size)];

    public void SetEntityId(Vector3Int position, int entityId) => _occupantEntityIds[position.FlatIndex(Size)] = entityId;

    /// <summary>Clears the cell only if it still records entityId. Returns whether it cleared anything.</summary>
    public bool ClearIfOccupiedBy(Vector3Int position, int entityId)
    {
        ref var occupantEntityId = ref _occupantEntityIds[position.FlatIndex(Size)];
        if (occupantEntityId != entityId)
        {
            return false;
        }

        occupantEntityId = -1;
        return true;
    }

    public int GetTerrainEntityId(int x, int y, TerrainLayer terrainLayer) => _terrainEntityIds[TerrainIndex(x, y, terrainLayer)];

    public void SetTerrainEntityId(int x, int y, TerrainLayer terrainLayer, int entityId) => _terrainEntityIds[TerrainIndex(x, y, terrainLayer)] = entityId;

    /// <summary>
    /// Ground and UnderGround each have a terrain floor beneath them; Flying is open air with
    /// none. The single source of truth for this mapping -- MapWindow (what to render) and
    /// SelectionWindowContent (what to inspect) both call this rather than each keeping their
    /// own copy.
    /// </summary>
    public static TerrainLayer? TerrainLayerFor(int mapLayer) => mapLayer switch
    {
        (int)MapLayer.UnderGround => TerrainLayer.UnderGround,
        (int)MapLayer.Ground => TerrainLayer.Ground,
        _ => null,
    };

    private int TerrainIndex(int x, int y, TerrainLayer terrainLayer) => new Vector3Int(x, y, (int)terrainLayer).FlatIndex(Size);
}