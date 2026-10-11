using System.Text.Json.Serialization;

namespace Game.Sprites;

/// <summary>A named sprite, with one or more candidate visual variants -- SpriteManifest.TryGetRandom picks one at random, TryGetFirst always takes Cells[0]. Deserialized directly from Content/SpriteManifest.json, so field names/shape must stay in sync with DevTools/SpriteManifestBuilder's own independent copy of this record.</summary>
/// <param name="PixelsPerTile">Source pixels per map tile; null stretches every cell to its footprint.</param>
/// <param name="PivotX">The source pixel, from a cell's left edge, that lands on the footprint's bottom-centre; null is the cell's horizontal centre.</param>
/// <param name="PivotY">The source pixel, from a cell's top edge, that lands on the footprint's bottom-centre; null is the cell's bottom edge.</param>
/// <param name="Linking">Cardinal makes Cells sets of 16 joined-sides pieces rather than interchangeable options.</param>
/// <param name="LinkGroup">Structures whose entries share a group join each other; null is the entry's own Name.</param>
/// <param name="Top">A wall's top face, authored apart from its face (this entry's own cells): drawn on the tile directly above the face's highest row.</param>
/// <param name="VariantWeights">For a linked part, each variant set's chance in whole percent, adding up to 100; null for equal chances (SpriteLinks.SetIndexFor). Top carries its own.</param>
/// <remarks>
/// Placement and linking belong to the entry rather than a cell because every option of an entry shares one canvas layout.
/// A linked set saved unfinished holds null for each empty slot, which every lookup treats as a miss. The builder also
/// writes the span it fills cells with (ColumnSpan/RowSpan, on the entry and on Top); that is authoring state only, which
/// these records don't declare, so deserialization skips it -- each cell carries its own span.
/// </remarks>
public sealed record SpriteManifestEntry(
    string Name,
    List<SpriteManifestCell?> Cells,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PixelsPerTile = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PivotX = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PivotY = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] SpriteLinking Linking = SpriteLinking.None,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LinkGroup = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SpriteManifestPart? Top = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<int>? VariantWeights = null)
{
    public const int CellsPerLinkedSet = 16;

    [JsonIgnore]
    public string EffectiveLinkGroup => LinkGroup ?? Name;

    [JsonIgnore]
    public string EffectiveTopLinkGroup => Top?.LinkGroup ?? Name;

    /// <summary>The entry's own cells and placement as a part: the whole picture, or a wall's face.</summary>
    [JsonIgnore]
    public SpriteManifestPart Face => new(Cells, PixelsPerTile, PivotX, PivotY, Linking, LinkGroup, VariantWeights);

    /// <summary>How cell draws relative to its footprint, or null when it stretches to fill it.</summary>
    public SpritePlacement? PlacementFor(SpriteManifestCell cell) =>
        PixelsPerTile is { } pixelsPerTile
            ? new SpritePlacement(pixelsPerTile, PivotX ?? cell.SourceWidth / 2, PivotY ?? cell.SourceHeight)
            : null;
}
