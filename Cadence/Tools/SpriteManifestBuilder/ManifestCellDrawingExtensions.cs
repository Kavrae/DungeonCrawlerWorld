namespace SpriteManifestBuilder;

/// <summary>The sprite format's values as WinForms draws them, and a cell as this tool describes it.</summary>
public static class ManifestCellDrawingExtensions
{
    public static Rectangle ToDrawingRectangle(this SpriteSourceRectangle rectangle) => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    public static Color ToDrawingColor(this SpriteColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    public static SpriteColor ToSpriteColor(this Color color) => new(color.R, color.G, color.B, color.A);

    public static Size SourceSize(this SpriteManifestCell cell) => new(cell.SourceWidth, cell.SourceHeight);

    public static string SpanText(this SpriteManifestCell cell) => $"{cell.EffectiveColumnSpan}x{cell.EffectiveRowSpan}";

    public static string Describe(this SpriteManifestCell cell)
    {
        var transform = new List<string>(4);
        if (cell.Rotation != 0)
        {
            transform.Add($"turned {cell.Rotation}°");
        }
        if (cell.FlipHorizontal)
        {
            transform.Add("flipped ↔");
        }
        if (cell.FlipVertical)
        {
            transform.Add("flipped ↕");
        }

        if (cell.Tint is not null)
        {
            transform.Add($"tinted {cell.Tint}");
        }

        var transformText = transform.Count == 0 ? "" : $", {string.Join(", ", transform)}";
        return $"{Path.GetFileName(cell.SheetPath)} ({cell.Column},{cell.Row}) {cell.SpanText()} cells of {cell.CellWidth}x{cell.CellHeight}{transformText}";
    }
}
