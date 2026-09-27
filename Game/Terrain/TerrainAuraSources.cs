using Engine.Math;
using Game.Modules.StatusEffectAura.Components;
using Game.World;

namespace Game.Terrain;

/// <summary>Terrain cells that radiate an aura, for anything that scatters aura sources into a grid -- StatusEffectAuraSystem's AuraGrid on the gameplay side, MapTintGrid's glow on the presentation side.</summary>
/// <remarks>Terrain auras have no entity to find through the aura-source pool, so both grids scan the terrain once when they build and then follow TerrainChangedEvent, TerrainLoadedEvent and TerrainUnloadingEvent.</remarks>
public static class TerrainAuraSources
{
    /// <summary>Calls visit with the position and aura of every terrain cell whose definition has one, on every MapLayer that has a floor. Does nothing at all when no registered terrain has an aura.</summary>
    public static void ForEach(IMapQuery map, TerrainRegistry registry, Action<Vector3Int, StatusEffectAuraSourceComponent> visit) =>
        ForEach(map, registry, map.Bounds, visit);

    /// <summary>ForEach, over only the cells of area (clipped to the map, and skipping unloaded neighborhoods, whose terrain reads as empty).</summary>
    public static void ForEach(IMapQuery map, TerrainRegistry registry, MapBounds area, Action<Vector3Int, StatusEffectAuraSourceComponent> visit)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(visit);

        var auras = AurasByTypeId(registry);
        if (auras is null)
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
                    if (typeId < auras.Length && auras[typeId] is { } aura)
                    {
                        visit(position, aura);
                    }
                }
            }
        }
    }

    /// <summary>The aura typeId radiates, if any.</summary>
    public static bool TryGetAura(TerrainRegistry registry, ushort typeId, out StatusEffectAuraSourceComponent aura)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (registry.TryGet(typeId, out var definition) && definition.Aura is { } found)
        {
            aura = found;
            return true;
        }

        aura = default;
        return false;
    }

    /// <summary>Every aura-radiating terrain cell of neighborhoodLayout, one list per row top to bottom -- in each row every MapLayer that has a floor, lowest first, then by column: the order ForEach visits the same cells in once the layout is loaded.</summary>
    /// <remarks>Reads only neighborhoodLayout and terrainRegistry, so a worker can list them while it plans a neighborhood, and loading doesn't have to scan for them.</remarks>
    public static IReadOnlyList<TerrainAuraCell>[] ByRow(NeighborhoodLayout neighborhoodLayout, TerrainRegistry terrainRegistry)
    {
        ArgumentNullException.ThrowIfNull(neighborhoodLayout);
        ArgumentNullException.ThrowIfNull(terrainRegistry);

        var auraCellsByRow = new IReadOnlyList<TerrainAuraCell>[neighborhoodLayout.MaxY - neighborhoodLayout.MinY];
        var aurasByTypeId = AurasByTypeId(terrainRegistry);
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
    private static StatusEffectAuraSourceComponent?[]? AurasByTypeId(TerrainRegistry registry)
    {
        var auras = new StatusEffectAuraSourceComponent?[registry.Count + 1];
        var any = false;
        for (var typeId = 1; typeId < auras.Length; typeId++)
        {
            if (TryGetAura(registry, (ushort)typeId, out var aura))
            {
                auras[typeId] = aura;
                any = true;
            }
        }

        return any ? auras : null;
    }
}
