using Microsoft.Xna.Framework;
using SpriteFormat;

namespace UIEngine.Rendering;

/// <summary>How a sprite is drawn into the rectangle it is given.</summary>
public enum SpriteFit : byte
{
    /// <summary>Stretched over the whole rectangle: the map, whose placement has already sized it (SpritePlacementMath).</summary>
    Fill,

    /// <summary>As large as fits inside the rectangle keeping its width-to-height ratio, on the bottom edge and centred across: every icon.</summary>
    Contain,
}

/// <summary>Where a sprite draws inside the rectangle it is given, for each SpriteFit.</summary>
/// <remarks>A quarter-turned cell fits by its turned size, since that is the shape it draws as.</remarks>
public static class SpriteFitMath
{
    public static (Vector2 TopLeft, Vector2 Size) Fitted(SpriteFit fit, Vector2 topLeft, Vector2 size, Point sourceSize, SpriteCellTransform transform) =>
        fit == SpriteFit.Contain ? Contain(topLeft, size, sourceSize, transform) : (topLeft, size);

    /// <summary>The largest rectangle with the drawn sprite's ratio inside the one given, on its bottom edge and centred across.</summary>
    public static (Vector2 TopLeft, Vector2 Size) Contain(Vector2 topLeft, Vector2 size, Point sourceSize, SpriteCellTransform transform)
    {
        var drawnWidth = transform.IsQuarterTurn ? sourceSize.Y : sourceSize.X;
        var drawnHeight = transform.IsQuarterTurn ? sourceSize.X : sourceSize.Y;
        if (drawnWidth <= 0 || drawnHeight <= 0)
        {
            return (topLeft, size);
        }

        var scale = System.Math.Min(size.X / drawnWidth, size.Y / drawnHeight);
        var fittedSize = new Vector2(drawnWidth * scale, drawnHeight * scale);
        return (new Vector2(topLeft.X + (size.X - fittedSize.X) / 2f, topLeft.Y + size.Y - fittedSize.Y), fittedSize);
    }
}
