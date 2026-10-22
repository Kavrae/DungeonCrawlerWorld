using System.Collections.Concurrent;
using Engine.Math;
using Game.Blueprints;
using Game.Modules.Core.Components;
using Game.Spawning;
using Game.Terrain;
using Game.World;

namespace Game.Areas;

/// <summary>Writes an area instance into a neighborhood's layout and plans what it spawns, as data: no pool, map or event, so it runs on a worker beside the rest of a neighborhood's plan.</summary>
/// <remarks>
/// A symbol's meaning comes from the area's legend over its palettes (AreaLegend), resolved to terrain and blueprint ids
/// once per definition and cached, so stamping is a walk over the rows. A space, and any part a legend entry leaves
/// out, keeps what generation wrote. Cells outside the layout (a bounded map's narrower edge neighborhood) are skipped.
/// Explicit placements are planned first, in their order, then each layer's symbol spawns row by row; each draws its
/// Chance, its OneOf and its entity seed from its own sequence (AreaSeeds), so one never shifts another. Content is
/// checked at build (AreaContentValidation), so every key and Guid here resolves.
/// </remarks>
public sealed class AreaStamp(AreaCatalog areas, TerrainRegistry terrain, BlueprintRegistry blueprints)
{
    private const int PercentRollRange = 100;

    private readonly ConcurrentDictionary<AreaDefinition, CompiledArea> _compiledAreas = new(ReferenceEqualityComparer.Instance);

    public void Apply(NeighborhoodLayout layout, AreaInstance instance, AreaStampResult result)
    {
        var area = instance.Definition;
        var compiled = _compiledAreas.GetOrAdd(area, Compile);
        var coverage = new bool[Enum.GetValues<MapLayer>().Length][];

        foreach (var layer in Enum.GetValues<MapLayer>())
        {
            if (area.Layers.RowsOf(layer) is { } rows)
            {
                coverage[(int)layer] = StampLayer(layout, instance, compiled, layer, rows);
            }
        }

        result.AddInstance(instance, coverage);
        PlanPlacements(layout, instance, compiled, result);
        PlanSymbolSpawns(layout, instance, compiled, result);

        foreach (var marker in area.Markers)
        {
            result.AddMarker(new AreaStampedMarker(instance.Key, marker, new Vector3Int(instance.OriginX + marker.X, instance.OriginY + marker.Y, (int)marker.Layer)));
        }
    }

    private static bool[] StampLayer(NeighborhoodLayout layout, AreaInstance instance, CompiledArea compiled, MapLayer layer, IReadOnlyList<string> rows)
    {
        var area = instance.Definition;
        var covered = new bool[area.Width * area.Height];
        var terrainLayer = Map.TerrainLayerFor((int)layer);
        var hasStructurePlane = (int)layer < layout.Depth;

        for (var y = 0; y < area.Height; y++)
        {
            var row = rows[y];
            for (var x = 0; x < area.Width; x++)
            {
                var symbol = row[x];
                var worldX = instance.OriginX + x;
                var worldY = instance.OriginY + y;
                if (symbol == AreaDefinition.TransparentSymbol || !IsInside(layout, worldX, worldY))
                {
                    continue;
                }

                covered[y * area.Width + x] = true;
                var cell = compiled.CellsBySymbol[symbol];
                if (terrainLayer is { } floorLayer && cell.TerrainTypeId is { } terrainTypeId)
                {
                    layout.SetTerrain(worldX, worldY, floorLayer, new TerrainCell(terrainTypeId));
                }

                if (hasStructurePlane && cell.StructureTypeId is { } structureTypeId)
                {
                    layout.SetStructure(new Vector3Int(worldX, worldY, (int)layer), new TerrainCell(structureTypeId));
                }
            }
        }

        return covered;
    }

    private void PlanPlacements(NeighborhoodLayout layout, AreaInstance instance, CompiledArea compiled, AreaStampResult result)
    {
        var area = instance.Definition;
        for (var index = 0; index < area.Entities.Count; index++)
        {
            var placement = area.Entities[index];
            var worldX = instance.OriginX + placement.X;
            var worldY = instance.OriginY + placement.Y;
            if (!IsInside(layout, worldX, worldY))
            {
                continue;
            }

            var rolls = new MathUtility(new SeededRandom(AreaSeeds.ForPlacement(instance.Seed, placement.PlacementId)));
            if (rolls.Next(0, PercentRollRange) >= placement.Chance)
            {
                continue;
            }

            var request = new SpawnRequest(compiled.PlacementBlueprintIds[index], worldX, worldY) { Layer = placement.Layer, Size = placement.Size, Seed = rolls.NextSeed() };
            if (!placement.Unique)
            {
                result.AddSpawn(new AreaPlannedSpawn(instance.Key, placement.PlacementId, request));
                continue;
            }

            SpawnRequest? fallback = placement.Fallback is { } fallbackBlueprint
                ? request with { BlueprintId = blueprints.GetId(fallbackBlueprint), Size = placement.Size }
                : null;
            var uniqueRequest = request with { Seed = AreaSeeds.ForUnique(area.Id, placement.PlacementId) };
            result.AddSpawn(new AreaPlannedSpawn(instance.Key, placement.PlacementId, uniqueRequest, placement.Blueprint, fallback));
        }
    }

    private static void PlanSymbolSpawns(NeighborhoodLayout layout, AreaInstance instance, CompiledArea compiled, AreaStampResult result)
    {
        var area = instance.Definition;
        foreach (var layer in Enum.GetValues<MapLayer>())
        {
            if (area.Layers.RowsOf(layer) is not { } rows)
            {
                continue;
            }

            for (var y = 0; y < area.Height; y++)
            {
                var row = rows[y];
                for (var x = 0; x < area.Width; x++)
                {
                    var symbol = row[x];
                    var worldX = instance.OriginX + x;
                    var worldY = instance.OriginY + y;
                    if (symbol == AreaDefinition.TransparentSymbol || !IsInside(layout, worldX, worldY))
                    {
                        continue;
                    }

                    var spawns = compiled.CellsBySymbol[symbol].Spawns;
                    for (var spawnIndex = 0; spawnIndex < spawns.Length; spawnIndex++)
                    {
                        var spawn = spawns[spawnIndex];
                        var rolls = new MathUtility(new SeededRandom(AreaSeeds.ForCell(instance.Seed, x, y, (int)layer, spawnIndex)));
                        if (rolls.Next(0, PercentRollRange) >= spawn.Chance)
                        {
                            continue;
                        }

                        var blueprintId = spawn.Choose(rolls);
                        var request = new SpawnRequest(blueprintId, worldX, worldY) { Layer = layer, Size = spawn.Size, Seed = rolls.NextSeed() };
                        result.AddSpawn(new AreaPlannedSpawn(instance.Key, PlacementId: null, request));
                    }
                }
            }
        }
    }

    private static bool IsInside(NeighborhoodLayout layout, int x, int y) =>
        x >= layout.MinX && y >= layout.MinY && x < layout.MaxX && y < layout.MaxY;

    private CompiledArea Compile(AreaDefinition area)
    {
        var cellsBySymbol = new Dictionary<char, CompiledCell>();
        foreach (var (symbol, cell) in AreaLegend.Resolve(area, areas.FindPalette))
        {
            cellsBySymbol[symbol] = new CompiledCell(
                cell.Terrain is { } terrainKey ? terrain.GetId(terrainKey) : null,
                cell.Structure is { } structureKey ? terrain.GetId(structureKey) : cell.ClearsStructure ? TerrainRegistry.None : null,
                [.. cell.SpawnList.Select(CompileSpawn)]);
        }

        return new CompiledArea(cellsBySymbol, [.. area.Entities.Select(placement => blueprints.GetId(placement.Blueprint))]);
    }

    private CompiledSpawn CompileSpawn(AreaSpawn spawn)
    {
        if (spawn.Blueprint is { } blueprint)
        {
            return new CompiledSpawn([blueprints.GetId(blueprint)], [1], spawn.Size, spawn.Chance);
        }

        return new CompiledSpawn(
            [.. spawn.OneOfList.Select(choice => blueprints.GetId(choice.Blueprint))],
            [.. spawn.OneOfList.Select(choice => choice.Weight)],
            spawn.Size,
            spawn.Chance);
    }

    private sealed record CompiledArea(Dictionary<char, CompiledCell> CellsBySymbol, ushort[] PlacementBlueprintIds);

    /// <param name="TerrainTypeId">The floor written, or null to keep generation's.</param>
    /// <param name="StructureTypeId">The wall written -- TerrainRegistry.None to clear one -- or null to keep generation's.</param>
    private sealed record CompiledCell(ushort? TerrainTypeId, ushort? StructureTypeId, CompiledSpawn[] Spawns);

    private sealed record CompiledSpawn(ushort[] BlueprintIds, int[] Weights, Vector2Byte? Size, byte Chance)
    {
        /// <summary>One blueprint by weight; a single choice still draws its roll, so adding a second choice later shifts nothing else.</summary>
        public ushort Choose(MathUtility rolls)
        {
            var roll = rolls.Next(0, Weights.Sum());
            for (var index = 0; index < Weights.Length; index++)
            {
                if (roll < Weights[index])
                {
                    return BlueprintIds[index];
                }

                roll -= Weights[index];
            }

            return BlueprintIds[^1];
        }
    }
}

/// <summary>Everything stamping one neighborhood's area instances decided: which cells each covers, what they spawn and where their markers are.</summary>
public sealed class AreaStampResult
{
    private readonly List<(AreaInstance Instance, bool[][] CoverageByLayer)> _instances = [];
    private readonly List<AreaInstance> _instanceList = [];
    private readonly List<AreaPlannedSpawn> _spawns = [];
    private readonly List<AreaStampedMarker> _markers = [];

    public IReadOnlyList<AreaInstance> Instances => _instanceList;

    /// <summary>What the instances spawn, in order: each instance's explicit placements, then its symbol spawns.</summary>
    public IReadOnlyList<AreaPlannedSpawn> Spawns => _spawns;

    public IReadOnlyList<AreaStampedMarker> Markers => _markers;

    /// <summary>Whether an instance defines the world cell (x, y) on layer -- a symbol other than space there -- so random population stays out of it.</summary>
    public bool Covers(int x, int y, MapLayer layer)
    {
        foreach (var (instance, coverageByLayer) in _instances)
        {
            if (instance.Contains(x, y) && coverageByLayer[(int)layer] is { } covered && covered[(y - instance.OriginY) * instance.Definition.Width + (x - instance.OriginX)])
            {
                return true;
            }
        }

        return false;
    }

    internal void AddInstance(AreaInstance instance, bool[][] coverageByLayer)
    {
        _instances.Add((instance, coverageByLayer));
        _instanceList.Add(instance);
    }

    internal void AddSpawn(AreaPlannedSpawn spawn) => _spawns.Add(spawn);

    internal void AddMarker(AreaStampedMarker marker) => _markers.Add(marker);
}

/// <summary>One spawn an area instance plans.</summary>
/// <param name="PlacementId">The explicit placement it comes from; null for a symbol's spawn.</param>
/// <param name="UniqueBlueprint">For a Unique placement, the blueprint the roster is asked about at spawn; Request spawns only if it is available, Fallback otherwise.</param>
/// <param name="Fallback">What a unique placement spawns instead, if anything.</param>
public readonly record struct AreaPlannedSpawn(AreaInstanceKey Instance, string? PlacementId, SpawnRequest Request, Guid? UniqueBlueprint = null, SpawnRequest? Fallback = null);

/// <summary>A marker of a stamped instance, at its world tile.</summary>
public readonly record struct AreaStampedMarker(AreaInstanceKey Instance, AreaMarker Marker, Vector3Int Position);
