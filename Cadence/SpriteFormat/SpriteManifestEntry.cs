using System.Text.Json.Serialization;

namespace SpriteFormat;

/// <summary>A named sprite: its cells (interchangeable options, or a linked set's pieces), how they are placed on the map, and, for a wall, its top.</summary>
/// <param name="PixelsPerTile">Source pixels per map tile; null stretches every cell to its footprint.</param>
/// <param name="PivotX">The source pixel, from a cell's left edge, that lands on the footprint's bottom-centre; null is the cell's horizontal centre.</param>
/// <param name="PivotY">The source pixel, from a cell's top edge, that lands on the footprint's bottom-centre; null is the cell's bottom edge.</param>
/// <param name="Linking">Cardinal or Horizontal makes Cells sets of joined-sides pieces rather than interchangeable options.</param>
/// <param name="Top">A wall's top face, authored apart from its face (this entry's own cells): drawn on the tile directly above the face's highest row.</param>
/// <param name="ColumnSpan">The span a tool fills this entry's cells with (0 means 1). Authoring only: the game ignores it, since each cell carries its own span.</param>
/// <param name="RowSpan">See ColumnSpan.</param>
/// <param name="VariantWeights">For a linked part, each variant set's chance in whole percent, adding up to 100; null for equal chances (SpriteLinks.SetIndexFor). Top carries its own.</param>
/// <remarks>
/// Placement and linking belong to the entry rather than a cell because every option of an entry shares one canvas layout.
/// A linked set saved unfinished holds null for each empty slot, which every lookup treats as a miss. Which structures join each
/// other is the game's to say, not the art's: the manifest only says how a piece looks for each combination of joined sides.
/// </remarks>
public sealed record SpriteManifestEntry(
    string Name,
    List<SpriteManifestCell?> Cells,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PixelsPerTile = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PivotX = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PivotY = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] SpriteLinking Linking = SpriteLinking.None,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SpriteManifestPart? Top = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ColumnSpan = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int RowSpan = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<int>? VariantWeights = null)
{
    /// <summary>The entry's own cells and placement as a part: the whole picture, or a wall's face.</summary>
    [JsonIgnore]
    public SpriteManifestPart Face => new(Cells, PixelsPerTile, PivotX, PivotY, Linking, ColumnSpan, RowSpan, VariantWeights);

    /// <summary>How cell draws relative to its footprint, or null when it stretches to fill it.</summary>
    public SpritePlacement? PlacementFor(SpriteManifestCell cell) =>
        PixelsPerTile is { } pixelsPerTile
            ? new SpritePlacement(pixelsPerTile, PivotX ?? cell.SourceWidth / 2, PivotY ?? cell.SourceHeight)
            : null;
}
