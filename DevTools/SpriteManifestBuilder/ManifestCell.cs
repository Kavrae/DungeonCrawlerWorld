using System.Text.Json.Serialization;

namespace SpriteManifestBuilder;

/// <summary>
/// One candidate spritesheet region for a ManifestEntry: ColumnSpan x RowSpan grid cells starting at
/// (Column, Row), flipped and then rotated clockwise by Rotation. Mirrors Game/Sprites/SpriteManifestCell.cs's
/// field shape/JSON property names exactly, but is a deliberately separate type -- this tool has no project
/// reference to Game.csproj, so the two must be kept in sync by hand if the shape ever changes.
/// </summary>
/// <remarks>
/// A span of 1 is always stored as 0 (absent from the JSON), so a one-cell option writes exactly what it did before spans existed.
/// The transform operations keep it canonical -- never both flips, which is a 180 rotation -- so two cells that look the same compare equal.
/// </remarks>
public sealed record ManifestCell(
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
    /// <summary>The colour the cell is multiplied by, parsed from Tint ("#RRGGBB" or "#RRGGBBAA"); null for none or a malformed one.</summary>
    [JsonIgnore]
    public Color? TintColor => Tint is { } text && Tints.TryParse(text, out var tint) ? tint : null;

    [JsonIgnore]
    public int EffectiveColumnSpan => Math.Max(1, ColumnSpan);

    [JsonIgnore]
    public int EffectiveRowSpan => Math.Max(1, RowSpan);

    [JsonIgnore]
    public Rectangle SourceRectangle => new(Column * CellWidth, Row * CellHeight, CellWidth * EffectiveColumnSpan, CellHeight * EffectiveRowSpan);

    [JsonIgnore]
    public string SpanText => $"{EffectiveColumnSpan}x{EffectiveRowSpan}";

    /// <summary>A quarter turn would change the drawn size, so the game allows it only on a square cell.</summary>
    [JsonIgnore]
    public bool CanQuarterTurn => SourceRectangle.Width == SourceRectangle.Height;

    [JsonIgnore]
    public bool IsTransformed => Rotation != 0 || FlipHorizontal || FlipVertical;

    /// <summary>The option covering every grid cell between two corners, in either order.</summary>
    public static ManifestCell Spanning(string sheetPath, Point firstCorner, Point secondCorner, int cellWidth, int cellHeight)
    {
        var column = Math.Min(firstCorner.X, secondCorner.X);
        var row = Math.Min(firstCorner.Y, secondCorner.Y);
        var columnSpan = Math.Abs(firstCorner.X - secondCorner.X) + 1;
        var rowSpan = Math.Abs(firstCorner.Y - secondCorner.Y) + 1;
        return new ManifestCell(sheetPath, column, row, cellWidth, cellHeight, columnSpan == 1 ? 0 : columnSpan, rowSpan == 1 ? 0 : rowSpan);
    }

    /// <summary>The picture as it is now, turned a further quarter clockwise.</summary>
    public ManifestCell RotatedClockwise() => WithTransform(Rotation + 90, FlipHorizontal, FlipVertical);

    public ManifestCell RotatedCounterClockwise() => WithTransform(Rotation + 270, FlipHorizontal, FlipVertical);

    /// <summary>The picture as it is now, mirrored left to right: mirroring a rotated picture reverses its rotation and toggles the source's horizontal flip.</summary>
    public ManifestCell MirroredHorizontally() => WithTransform(360 - Rotation, !FlipHorizontal, FlipVertical);

    public ManifestCell MirroredVertically() => WithTransform(360 - Rotation, FlipHorizontal, !FlipVertical);

    public ManifestCell WithoutTransform() => this with { Rotation = 0, FlipHorizontal = false, FlipVertical = false };

    public string Describe()
    {
        var transform = new List<string>(3);
        if (Rotation != 0)
        {
            transform.Add($"turned {Rotation}°");
        }
        if (FlipHorizontal)
        {
            transform.Add("flipped ↔");
        }
        if (FlipVertical)
        {
            transform.Add("flipped ↕");
        }

        if (Tint is not null)
        {
            transform.Add($"tinted {Tint}");
        }

        var transformText = transform.Count == 0 ? "" : $", {string.Join(", ", transform)}";
        return $"{Path.GetFileName(SheetPath)} ({Column},{Row}) {SpanText} cells of {CellWidth}x{CellHeight}{transformText}";
    }

    private ManifestCell WithTransform(int rotation, bool flipHorizontal, bool flipVertical)
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
