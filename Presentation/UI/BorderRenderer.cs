using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.UI;

/// <summary>
/// Draws a border's four edges (see BorderThickness.GetEdgeRectangles for the geometry),
/// either as one flat color or as a light/dark bevel -- shared by Window and Button so both
/// get the same Outset/Inset look instead of each hand-rolling its own shading.
/// </summary>
public static class BorderRenderer
{
    private static readonly Color LightBevelColor = Color.White;
    private static readonly Color DarkBevelColor = Color.Black;

    /// <summary>flatColor only affects BorderStyle.Flat -- Outset/Inset always use the fixed light/dark bevel pair above, an independent two-color shading effect rather than a single overridable color.</summary>
    public static void Draw(SpriteBatch spriteBatch, Texture2D unitRectangle, BorderStyle style, Color flatColor, Rectangle top, Rectangle bottom, Rectangle left, Rectangle right)
    {
        switch (style)
        {
            case BorderStyle.Flat:
                spriteBatch.Draw(unitRectangle, top, flatColor);
                spriteBatch.Draw(unitRectangle, bottom, flatColor);
                spriteBatch.Draw(unitRectangle, left, flatColor);
                spriteBatch.Draw(unitRectangle, right, flatColor);
                break;

            case BorderStyle.Outset:
                // Raised look: light catches the top-left as if lit from above, dark shadows the bottom-right.
                spriteBatch.Draw(unitRectangle, top, LightBevelColor);
                spriteBatch.Draw(unitRectangle, left, LightBevelColor);
                spriteBatch.Draw(unitRectangle, bottom, DarkBevelColor);
                spriteBatch.Draw(unitRectangle, right, DarkBevelColor);
                break;

            case BorderStyle.Inset:
                // Pressed look: same lighting, reversed -- top-left now in shadow, bottom-right catching light.
                spriteBatch.Draw(unitRectangle, top, DarkBevelColor);
                spriteBatch.Draw(unitRectangle, left, DarkBevelColor);
                spriteBatch.Draw(unitRectangle, bottom, LightBevelColor);
                spriteBatch.Draw(unitRectangle, right, LightBevelColor);
                break;
        }
    }
}