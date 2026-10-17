namespace SpriteManifestBuilder;

/// <summary>The small warning marker drawn on a slot, an option or an existing entry whose cells don't fit the span.</summary>
public static class WarningIcon
{
    public const int Pixels = 16;

    private static readonly Lazy<Bitmap> SharedBitmap = new(static () => new Icon(SystemIcons.Warning, Pixels, Pixels).ToBitmap());

    public static void Draw(Graphics graphics, int x, int y) => graphics.DrawImage(SharedBitmap.Value, x, y, Pixels, Pixels);
}
