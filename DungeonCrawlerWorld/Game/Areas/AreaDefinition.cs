using Engine.Tags;
using Game.Modules.Core.Components;

namespace Game.Areas;

/// <summary>A named piece of map -- terrain, walls, entities and markers, as rows of symbols per layer and a legend -- stamped into the world wherever an instance of it is placed.</summary>
/// <remarks>
/// Coordinates are x right and y down from the area's top-left tile, as the map's. Nothing here names a world position:
/// where an area goes is its placement's (AreaPlacement), and what happened to one stamp of it is its instance's
/// (AreaInstance). A symbol's meaning is its legend entry: the area's own Legend over its Palettes, a later palette
/// over an earlier one (AreaLegend.Resolve).
/// </remarks>
/// <param name="Layers">Each layer's rows; Ground is required, a missing layer keeps what generation put there.</param>
/// <param name="Palettes">Shared legends, applied in order.</param>
/// <param name="Entities">Placements of one blueprint each at one tile, for anything named, large, stacked or that later content points at.</param>
public sealed record AreaDefinition(
    Guid Id,
    string Name,
    string Description,
    GameplayTagSet Tags,
    int Width,
    int Height,
    AreaLayers Layers,
    IReadOnlyList<AreaCell> Legend,
    IReadOnlyList<Guid> Palettes,
    IReadOnlyList<AreaEntityPlacement> Entities,
    IReadOnlyList<AreaMarker> Markers)
{
    /// <summary>The symbol that keeps whatever generation put in a cell: the area is transparent there.</summary>
    public const char TransparentSymbol = ' ';
}

/// <summary>An area's rows per map layer: Height strings of Width symbols each.</summary>
/// <param name="UnderGround">Null when the area leaves the layer as generated.</param>
/// <param name="Flying">Null when the area leaves the layer as generated; a Flying cell has no floor.</param>
public sealed record AreaLayers(IReadOnlyList<string> Ground, IReadOnlyList<string>? UnderGround = null, IReadOnlyList<string>? Flying = null)
{
    /// <summary>layer's rows, or null when the area leaves that layer as generated.</summary>
    public IReadOnlyList<string>? RowsOf(MapLayer layer) => layer switch
    {
        MapLayer.UnderGround => UnderGround,
        MapLayer.Ground => Ground,
        MapLayer.Flying => Flying,
        _ => null,
    };
}
