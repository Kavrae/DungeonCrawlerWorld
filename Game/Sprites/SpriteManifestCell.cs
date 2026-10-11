using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

namespace Game.Sprites;

/// <summary>One candidate spritesheet region for a SpriteManifestEntry: the rectangle of ColumnSpan x RowSpan grid cells starting at (Column, Row), optionally flipped and then rotated.</summary>
/// <remarks>
/// A span of 0 (absent from the JSON) means 1, so entries written before spans existed read unchanged; the builder writes 1 as absent for the same reason.
/// The transform is applied as SpriteBatch applies one: flips first, then Rotation clockwise about the centre. Rotation is 0, 90, 180 or 270, and 90/270 need a square
/// source so the drawn size doesn't change. Pivot and placement are measured on the transformed picture. The builder keeps it canonical: both flips together are written
/// as a 180 rotation instead.
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
    /// <summary>The colour the cell's pixels are multiplied by ("#RRGGBB" or "#RRGGBBAA"), or null for none. An option's cells share one; a linked set's builder writes the set's onto each of its cells.</summary>
    [JsonIgnore]
    public Color? TintColor => Tint is { } text && SpriteTint.TryParse(text, out var tint) ? tint : null;

    [JsonIgnore]
    public int EffectiveColumnSpan => System.Math.Max(1, ColumnSpan);

    [JsonIgnore]
    public int EffectiveRowSpan => System.Math.Max(1, RowSpan);

    [JsonIgnore]
    public int SourceWidth => CellWidth * EffectiveColumnSpan;

    [JsonIgnore]
    public int SourceHeight => CellHeight * EffectiveRowSpan;

    [JsonIgnore]
    public Rectangle SourceRectangle => new(Column * CellWidth, Row * CellHeight, SourceWidth, SourceHeight);
}
