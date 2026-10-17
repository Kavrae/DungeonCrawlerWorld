using System.Text.Json.Serialization;

namespace SpriteFormat;

/// <summary>One separately authored picture of a manifest entry: its cells, how they are placed on the map, and how they join their neighbours.</summary>
/// <param name="ColumnSpan">The span a tool fills this part's cells with (0 means 1). Authoring only: the game ignores it, since each cell carries its own span.</param>
/// <param name="RowSpan">See ColumnSpan.</param>
/// <remarks>An entry's own fields are its first part (a wall's face); SpriteManifestEntry.Top is a second one. Fields mean what they mean on SpriteManifestEntry.</remarks>
public sealed record SpriteManifestPart(
    List<SpriteManifestCell?> Cells,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PixelsPerTile = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PivotX = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PivotY = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] SpriteLinking Linking = SpriteLinking.None,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ColumnSpan = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int RowSpan = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<int>? VariantWeights = null)
{
    /// <summary>How cell draws relative to its anchor tile, or null when it stretches to fill it.</summary>
    public SpritePlacement? PlacementFor(SpriteManifestCell cell) =>
        PixelsPerTile is { } pixelsPerTile
            ? new SpritePlacement(pixelsPerTile, PivotX ?? cell.SourceWidth / 2, PivotY ?? cell.SourceHeight)
            : null;

    /// <summary>How many rows this part covers at a 1x1 footprint: its own tile plus every row it reaches above, a part row counting whole.</summary>
    public int HeightRows()
    {
        var rowsAbove = 0d;
        foreach (var cell in Cells)
        {
            if (cell is not null && PlacementFor(cell) is { } placement)
            {
                rowsAbove = Math.Max(rowsAbove, SpriteReach.Of(cell.SourceWidth, cell.SourceHeight, placement).RowsAbove);
            }
        }

        return 1 + (int)Math.Ceiling(rowsAbove);
    }
}
