using Builders.References;
using Engine.Tags;
using Game.Areas;

namespace Builders.MapAreas;

/// <summary>One row of the Map tab: an area, a palette or a fixed placement, with the name it is listed under.</summary>
public sealed record MapContentRecord(ContentRecordKind Kind, Guid Id, string Name, GameplayTagSet Tags)
{
    public AreaDefinition? Area { get; init; }

    public AreaPalette? Palette { get; init; }

    public AreaPlacement? Placement { get; init; }

    /// <summary>Every area, palette and placement in areas, each once, areas first -- built once, so selection by reference holds.</summary>
    public static IReadOnlyList<MapContentRecord> AllIn(AreaCatalog areas)
    {
        var records = new List<MapContentRecord>();
        foreach (var area in areas.Areas.Definitions)
        {
            records.Add(new MapContentRecord(ContentRecordKind.Area, area.Id, $"{area.Name} ({area.Width}x{area.Height})", area.Tags) { Area = area });
        }

        foreach (var palette in areas.Palettes.Definitions)
        {
            records.Add(new MapContentRecord(ContentRecordKind.AreaPalette, palette.Id, palette.Name, GameplayTagSet.Empty) { Palette = palette });
        }

        foreach (var placement in areas.Placements.Definitions)
        {
            records.Add(new MapContentRecord(ContentRecordKind.AreaPlacement, placement.Id, PlacementName(placement, areas), GameplayTagSet.Empty) { Placement = placement });
        }

        return records;
    }

    /// <summary>"Starting area at (0, 0) +0,+0": the area it places, its neighborhood and the offset in it.</summary>
    public static string PlacementName(AreaPlacement placement, AreaCatalog areas)
    {
        var areaName = areas.Areas.TryGet(placement.Area, out var area) ? area.Name : "Missing area";
        return $"{areaName} at ({placement.NeighborhoodX}, {placement.NeighborhoodY}) +{placement.OffsetX},+{placement.OffsetY}";
    }
}
