using Engine.Math;
using Game.Areas;
using Game.Modules.Core.Components;
using Game.World;

namespace Builders.MapAreas;

/// <summary>What the Map tab's hover readout says about one tile of a preview build: where it is, the area it is in and its symbol there, its floor and wall, who stands there, and which placements and markers name it.</summary>
public static class AreaCellReadout
{
    public const int LineCount = 5;

    private const string NoneText = "none";

    /// <summary>Exactly LineCount lines about the map tile (x, y) on layer, so the readout never changes height.</summary>
    /// <remarks>A scene of one area names its cells by their place in it; a neighborhood by world position, then the area holding it, if any.</remarks>
    public static IReadOnlyList<string> Describe(IMapPreviewScene scene, int x, int y, MapLayer layer)
    {
        var session = scene.Session;
        var world = session.World;
        var terrain = session.Catalogs.Terrain;
        var position = new Vector3Int(x, y, (int)layer);

        var instance = scene.Area ?? scene.Stamp.Instances.FirstOrDefault(stamped => stamped.Contains(x, y));
        var placeText = instance is null
            ? "generated (no area)"
            : SymbolText(instance, x - instance.OriginX, y - instance.OriginY, layer);
        if (scene.Area is null && instance is not null)
        {
            placeText = $"{instance.Definition.Name} ({x - instance.OriginX}, {y - instance.OriginY}), {placeText}";
        }

        var floorText = Map.TerrainLayerFor((int)layer) is null
            ? "no floor"
            : terrain.TryGet(world.GetTerrainAt(position).TypeId, out var floor) ? floor.Name : NoneText;
        var wallText = terrain.TryGet(world.GetStructureAt(position).TypeId, out var wall) ? wall.Name : NoneText;

        var naming = session.Views.EntityNaming;
        var entityIds = new List<int>();
        var blockingEntityId = world.GetEntityIdAt(position);
        if (blockingEntityId >= 0)
        {
            entityIds.Add(blockingEntityId);
        }

        entityIds.AddRange(world.GetOccupantEntityIdsAt(position).Where(entityId => entityId != blockingEntityId));
        var entitiesText = entityIds.Count == 0 ? NoneText : string.Join(", ", entityIds.Select(entityId => naming.NameOf(entityId)));

        var placementIds = scene.Stamp.Spawns
            .Where(spawn => spawn.PlacementId is not null && Covers(spawn, position, session))
            .Select(spawn => spawn.PlacementId!)
            .ToList();
        var markerIds = scene.Stamp.Markers.Where(marker => marker.Position == position).Select(marker => marker.Marker.MarkerId).ToList();

        return
        [
            $"({x}, {y}) on {layer}: {placeText}",
            $"Floor: {floorText}    Wall: {wallText}",
            $"Entities: {entitiesText}",
            $"Placements: {(placementIds.Count == 0 ? NoneText : string.Join(", ", placementIds))}",
            $"Markers: {(markerIds.Count == 0 ? NoneText : string.Join(", ", markerIds))}",
        ];
    }

    /// <summary>Lines for no cell, the same count.</summary>
    public static IReadOnlyList<string> Nothing { get; } = ["Point at a cell to see what is there.", string.Empty, string.Empty, string.Empty, string.Empty];

    private static string SymbolText(AreaInstance instance, int areaX, int areaY, MapLayer layer)
    {
        var rows = instance.Definition.Layers.RowsOf(layer);
        var symbol = rows is null ? AreaDefinition.TransparentSymbol : rows[areaY][areaX];
        return symbol == AreaDefinition.TransparentSymbol ? "transparent (keeps what generation made)" : $"symbol '{symbol}'";
    }

    private static bool Covers(AreaPlannedSpawn spawn, Vector3Int position, Game.Bootstrap.GameSession session)
    {
        var request = spawn.Request;
        var definitions = session.Catalogs.Definitions;
        var size = request.Size ?? definitions.Resolve(request.BlueprintId).Size;
        var layer = (int)(request.Layer ?? definitions.Resolve(request.BlueprintId).Layer);
        return layer == position.Z && position.X >= request.X && position.X < request.X + size.X && position.Y >= request.Y && position.Y < request.Y + size.Y;
    }
}
