using Builders.Browsing;
using Builders.References;
using Engine.Tags;
using Game.Areas;

namespace Builders.MapAreas;

/// <summary>The Map browser's groups: areas a fixed placement puts in the world, the rest of the areas, then palettes and placements.</summary>
public static class MapBrowserSections
{
    public const string PlacedHeading = "Placed areas";
    public const string UnplacedHeading = "Unplaced areas";
    public const string PalettesHeading = "Palettes";
    public const string PlacementsHeading = "Placements";

    public static IReadOnlyList<string> HeadingsInOrder { get; } = [PlacedHeading, UnplacedHeading, PalettesHeading, PlacementsHeading];

    public static IReadOnlyList<RecordBrowserSection<MapContentRecord>> Build(IEnumerable<MapContentRecord> records, AreaCatalog areas, string search, GameplayTagRegistry gameplayTags)
    {
        var placedAreaIds = areas.Placements.Definitions.Select(placement => placement.Area).ToHashSet();
        return RecordBrowserSections.Build(records, HeadingsInOrder, record => HeadingOf(record, placedAreaIds), static record => record.Name, static record => record.Tags, search, gameplayTags);
    }

    private static string HeadingOf(MapContentRecord record, HashSet<Guid> placedAreaIds) => record.Kind switch
    {
        ContentRecordKind.Area => placedAreaIds.Contains(record.Id) ? PlacedHeading : UnplacedHeading,
        ContentRecordKind.AreaPalette => PalettesHeading,
        _ => PlacementsHeading,
    };
}
