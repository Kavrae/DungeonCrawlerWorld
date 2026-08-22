using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>
/// Draws one bordered square -- an outer border-colored rect, then a slightly inset fill-colored
/// rect on top -- the shared box look TargetShapePreviewElement's own shape-grid cells and
/// RangeIndicatorElement's range-ruler boxes both use, so the two rows read as the same visual
/// language rather than two independently-tuned border/fill recipes.
/// </summary>
public static class BorderedBoxRenderer
{
    public static void Draw(SpriteBatch spriteBatch, Texture2D unitRectangle, Vector2 origin, float outerSize, float borderThickness, Color borderColor, Color fillColor)
    {
        spriteBatch.Draw(unitRectangle, new Rectangle((int)origin.X, (int)origin.Y, (int)outerSize, (int)outerSize), borderColor);

        var innerSize = outerSize - borderThickness * 2;
        if (innerSize > 0)
        {
            spriteBatch.Draw(unitRectangle, new Rectangle((int)(origin.X + borderThickness), (int)(origin.Y + borderThickness), (int)innerSize, (int)innerSize), fillColor);
        }
    }
}
