using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>
/// Draws a grid of colored tile backgrounds. Decoupled from Map/World -- the caller supplies
/// a flat, column-major color array (backgroundColors[column + row * columns], matching
/// MapWindow's cache layout) rather than this type knowing anything about map nodes or
/// entities. Per-tile highlighting (e.g. a selected-tile outline) is the caller's concern,
/// layered on top of this.
/// </summary>
public sealed class TileRenderer
{
    private const int GridLineThickness = 1;

    public void DrawBackgrounds(
        SpriteBatch spriteBatch,
        Texture2D unitRectangle,
        ReadOnlySpan<Color> backgroundColors,
        int columns,
        int rows,
        Point tileSize,
        Vector2 pixelOffset = default)
    {
        for (var column = 0; column < columns; column++)
        {
            for (var row = 0; row < rows; row++)
            {
                var destination = new Rectangle(
                    (int)(column * tileSize.X - pixelOffset.X),
                    (int)(row * tileSize.Y - pixelOffset.Y),
                    tileSize.X - GridLineThickness,
                    tileSize.Y - GridLineThickness);
                spriteBatch.Draw(unitRectangle, destination, backgroundColors[column + row * columns]);
            }
        }
    }
}