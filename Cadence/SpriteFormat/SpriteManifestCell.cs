using System.Text.Json.Serialization;

namespace SpriteFormat;

/// <summary>One candidate spritesheet region for a manifest part: the rectangle of ColumnSpan x RowSpan grid cells starting at (Column, Row), optionally flipped and then rotated, and tinted.</summary>
/// <remarks>
/// A span of 0 (absent from the JSON) means 1, so entries written before spans existed read unchanged; a span of 1 is written as
/// absent for the same reason. The transform is applied as a sprite batch applies one: flips first, then Rotation clockwise about the
/// centre. Rotation is 0, 90, 180 or 270, and 90/270 need a square source so the drawn size doesn't change. Pivot and placement are
/// measured on the transformed picture. The transform operations keep it canonical -- never both flips, which is a 180 rotation -- so
/// two cells that look the same compare equal.
/// </remarks>
public sealed record SpriteManifestCell(
    string SheetPath,
    int Column,
    int Row,
    int CellWidth,
    int CellHeight,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ColumnSpan = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int RowSpan = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Rotation = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool FlipHorizontal = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool FlipVertical = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tint = null)
{
    /// <summary>The colour the cell's pixels are multiplied by, parsed from Tint ("#RRGGBB" or "#RRGGBBAA"); null for none or a malformed one. A linked set's cells share their set's.</summary>
    [JsonIgnore]
    public SpriteColor? TintColor => Tint is { } text && SpriteTint.TryParse(text, out var tint) ? tint : null;

    [JsonIgnore]
    public int EffectiveColumnSpan => Math.Max(1, ColumnSpan);

    [JsonIgnore]
    public int EffectiveRowSpan => Math.Max(1, RowSpan);

    [JsonIgnore]
    public int SourceWidth => CellWidth * EffectiveColumnSpan;

    [JsonIgnore]
    public int SourceHeight => CellHeight * EffectiveRowSpan;

    [JsonIgnore]
    public SpriteSourceRectangle SourceRectangle => new(Column * CellWidth, Row * CellHeight, SourceWidth, SourceHeight);

    /// <summary>A quarter turn would change the drawn size, so it is allowed only on a square cell.</summary>
    [JsonIgnore]
    public bool CanQuarterTurn => SourceWidth == SourceHeight;

    [JsonIgnore]
    public bool IsTransformed => Rotation != 0 || FlipHorizontal || FlipVertical;

    /// <summary>The cell covering every grid cell between two corners, in either order, with a span of 1 written as absent.</summary>
    public static SpriteManifestCell Spanning(string sheetPath, int firstColumn, int firstRow, int secondColumn, int secondRow, int cellWidth, int cellHeight)
    {
        var columnSpan = Math.Abs(firstColumn - secondColumn) + 1;
        var rowSpan = Math.Abs(firstRow - secondRow) + 1;
        return new SpriteManifestCell(
            sheetPath,
            Math.Min(firstColumn, secondColumn),
            Math.Min(firstRow, secondRow),
            cellWidth,
            cellHeight,
            columnSpan == 1 ? 0 : columnSpan,
            rowSpan == 1 ? 0 : rowSpan);
    }

    /// <summary>The picture as it is now, turned a further quarter clockwise.</summary>
    public SpriteManifestCell RotatedClockwise() => WithTransform(Rotation + 90, FlipHorizontal, FlipVertical);

    public SpriteManifestCell RotatedCounterClockwise() => WithTransform(Rotation + 270, FlipHorizontal, FlipVertical);

    /// <summary>The picture as it is now, mirrored left to right: mirroring a rotated picture reverses its rotation and toggles the source's horizontal flip.</summary>
    public SpriteManifestCell MirroredHorizontally() => WithTransform(360 - Rotation, !FlipHorizontal, FlipVertical);

    public SpriteManifestCell MirroredVertically() => WithTransform(360 - Rotation, FlipHorizontal, !FlipVertical);

    public SpriteManifestCell WithoutTransform() => this with { Rotation = 0, FlipHorizontal = false, FlipVertical = false };

    private SpriteManifestCell WithTransform(int rotation, bool flipHorizontal, bool flipVertical)
    {
        if (flipHorizontal && flipVertical)
        {
            rotation += 180;
            flipHorizontal = false;
            flipVertical = false;
        }

        return this with { Rotation = ((rotation % 360) + 360) % 360, FlipHorizontal = flipHorizontal, FlipVertical = flipVertical };
    }
}
