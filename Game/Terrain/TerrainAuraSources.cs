using Engine.Math;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.World;

namespace Game.Terrain;

/// <summary>Terrain cells that radiate an aura, for the AuraField, which scatters them into its grid.</summary>
/// <remarks>Terrain auras have no entity to find through the aura-source pool, so the field scans the terrain once when it is built and then follows TerrainChangedEvent, TerrainLoadedEvent and TerrainUnloadingEvent.</remarks>
public static class TerrainAuraSources
{
    /// <summary>Calls visit with the position and aura of every terrain cell whose definition has one, on every MapLayer that has a floor. Does nothing at all when no registered terrain has an aura.</summary>
    public static void ForEach(IMapQuery map, TerrainRegistry registry, AuraCatalog auras, Action<Vector3Int, AuraSourceComponent> visit) =>
        ForEach(map, registry, auras, map.Bounds, visit);

    /// <summary>ForEach, over only the cells of area (clipped to the map, and skipping unloaded neighborhoods, whose terrain reads as empty).</summary>
    public static void ForEach(IMapQuery map, TerrainRegistry registry, AuraCatalog auras, MapBounds area, Action<Vector3Int, AuraSourceComponent> visit)
    {
        var aurasByTypeId = AurasByTypeId(registry, auras);
        if (aurasByTypeId is null)
        {
            return;
        }

        var mapBounds = map.Bounds;
        var bounds = new MapBounds(System.Math.Max(area.MinX, mapBounds.MinX), System.Math.Max(area.MinY, mapBounds.MinY), System.Math.Min(area.MaxX, mapBounds.MaxX), System.Math.Min(area.MaxY, mapBounds.MaxY), mapBounds.Depth);
        for (var z = 0; z < bounds.Depth; z++)
        {
            if (Map.TerrainLayerFor(z) is null)
            {
                continue;
            }

            for (var y = bounds.MinY; y < bounds.MaxY; y++)
            {
                for (var x = bounds.MinX; x < bounds.MaxX; x++)
                {
                    var position = new Vector3Int(x, y, z);
                    if (!map.IsOnMap(position))
                    {
                        x = System.Math.Min(bounds.MaxX, Neighborhoods.OriginOf(Neighborhoods.CellOf(x) + 1)) - 1;
                        continue;
                    }

                    var typeId = map.GetTerrainAt(position).TypeId;
                    if (typeId < aurasByTypeId.Length && aurasByTypeId[typeId] is { } aura)
                    {
                        visit(position, aura);
                    }
                }
            }
        }
    }

    /// <summary>The aura typeId radiates, if any.</summary>
    public static bool TryGetAura(TerrainRegistry registry, AuraCatalog auras, ushort typeId, out AuraSourceComponent aura)
    {
        if (registry.TryGet(typeId, out var definition) && definition.Aura is { } found)
        {
            aura = new AuraSourceComponent(auras.Register(found.Aura), found.Power, found.Size);
            return true;
        }

        aura = default;
        return false;
    }

    /// <summary>Every aura-radiating terrain cell of neighborhoodLayout, one list per row top to bottom -- in each row every MapLayer that has a floor, lowest first, then by column: the order ForEach visits the same cells in once the layout is loaded.</summary>
    /// <remarks>Reads only neighborhoodLayout, terrainRegistry and auras, so a worker can list them while it plans a neighborhood, and loading doesn't have to scan for them.</remarks>
    public static IReadOnlyList<TerrainAuraCell>[] ByRow(NeighborhoodLayout neighborhoodLayout, TerrainRegistry terrainRegistry, AuraCatalog auras)
    {
        var auraCellsByRow = new IReadOnlyList<TerrainAuraCell>[neighborhoodLayout.MaxY - neighborhoodLayout.MinY];
        var aurasByTypeId = AurasByTypeId(terrainRegistry, auras);
        for (var row = neighborhoodLayout.MinY; row < neighborhoodLayout.MaxY; row++)
        {
            var rowAuraCells = new List<TerrainAuraCell>();
            for (var mapLayer = 0; aurasByTypeId is not null && mapLayer < neighborhoodLayout.Depth; mapLayer++)
            {
                if (Map.TerrainLayerFor(mapLayer) is not { } terrainLayer)
                {
                    continue;
                }

                for (var column = neighborhoodLayout.MinX; column < neighborhoodLayout.MaxX; column++)
                {
                    var terrainTypeId = neighborhoodLayout.GetTerrain(column, row, terrainLayer).TypeId;
                    if (terrainTypeId < aurasByTypeId.Length && aurasByTypeId[terrainTypeId] is { } aura)
                    {
                        rowAuraCells.Add(new TerrainAuraCell(new Vector3Int(column, row, mapLayer), aura));
                    }
                }
            }

            auraCellsByRow[row - neighborhoodLayout.MinY] = rowAuraCells;
        }

        return auraCellsByRow;
    }

    /// <summary>Each registered id's aura, indexed by id, or null when no terrain has one -- resolved once per scan so the per-cell check is an array read.</summary>
    private static AuraSourceComponent?[]? AurasByTypeId(TerrainRegistry registry, AuraCatalog auras)
    {
        var aurasByTypeId = new AuraSourceComponent?[registry.Count + 1];
        var any = false;
        for (var typeId = 1; typeId < aurasByTypeId.Length; typeId++)
        {
            if (TryGetAura(registry, auras, (ushort)typeId, out var aura))
            {
                aurasByTypeId[typeId] = aura;
                any = true;
            }
        }

        return any ? aurasByTypeId : null;
    }
}
