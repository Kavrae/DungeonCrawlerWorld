using Engine.Math;
using Game.Modules.Core.Components;
using Game.Terrain;

namespace Game.World;

/// <summary>One neighborhood's terrain and structures, written before the neighborhood is part of the map, then handed to Map.LoadNeighborhood whole.</summary>
/// <remarks>
/// Made by Map.CreateLayout. It is its own set of stores, not a view onto the map, so it can be filled
/// on a worker thread while the map is in use. Positions are world coordinates inside the neighborhood.
/// </remarks>
public sealed class NeighborhoodLayout
{
    internal NeighborhoodLayout(int cellX, int cellY, MapNeighborhood neighborhoodCellStores)
    {
        CellX = cellX;
        CellY = cellY;
        NeighborhoodCellStores = neighborhoodCellStores;
    }

    public int CellX { get; }

    public int CellY { get; }

    /// <summary>The first column it covers.</summary>
    public int MinX => Neighborhoods.OriginOf(CellX);

    /// <summary>The first row it covers.</summary>
    public int MinY => Neighborhoods.OriginOf(CellY);

    /// <summary>One past the last column: a neighborhood cut off by a bounded map's edge is narrower.</summary>
    public int MaxX => MinX + NeighborhoodCellStores.Width;

    /// <summary>One past the last row.</summary>
    public int MaxY => MinY + NeighborhoodCellStores.Height;

    /// <summary>How many MapLayers it covers: the map's depth.</summary>
    public int Depth => NeighborhoodCellStores.StructureTypeIds.Length / NeighborhoodCellStores.PlaneSize;

    /// <summary>The per-cell stores (terrain, structures, and the still-empty occupant indexes) this layout writes and the map takes over when the neighborhood loads.</summary>
    internal MapNeighborhood NeighborhoodCellStores { get; }

    public TerrainCell GetTerrain(int x, int y, TerrainLayer terrainLayer)
    {
        var terrainIndex = Map.TerrainIndex(NeighborhoodCellStores, x, y, terrainLayer);
        return new TerrainCell(NeighborhoodCellStores.TerrainTypeIds[terrainIndex], NeighborhoodCellStores.TerrainVariants[terrainIndex]);
    }

    public void SetTerrain(int x, int y, TerrainLayer terrainLayer, TerrainCell terrainCell)
    {
        var terrainIndex = Map.TerrainIndex(NeighborhoodCellStores, x, y, terrainLayer);
        NeighborhoodCellStores.TerrainTypeIds[terrainIndex] = terrainCell.TypeId;
        NeighborhoodCellStores.TerrainVariants[terrainIndex] = terrainCell.Variant;
    }

    public void SetStructure(Vector3Int position, TerrainCell structureCell)
    {
        var structureIndex = Map.CellIndex(NeighborhoodCellStores, position);
        NeighborhoodCellStores.StructureTypeIds[structureIndex] = structureCell.TypeId;
        NeighborhoodCellStores.StructureVariants[structureIndex] = structureCell.Variant;
    }
}
