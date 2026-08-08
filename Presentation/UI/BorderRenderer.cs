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
    private static readonly Color ContrastInnerColor = Color.LightGray;
    private static readonly Color ContrastOuterColor = Color.Black;

    /// <summary>flatColor only affects BorderStyle.Flat -- Outset/Inset/FlatContrast always use their own fixed color pairs, an independent two-color shading effect rather than a single overridable color. alphaMultiplier defaults to fully opaque -- HotbarContent's disabled-slot treatment is the one caller that passes a lower value, to fade a FlatContrast slot border along with everything else on that slot.</summary>
    public static void Draw(SpriteBatch spriteBatch, Texture2D unitRectangle, BorderStyle style, Color flatColor, Rectangle top, Rectangle bottom, Rectangle left, Rectangle right, float alphaMultiplier = 1f)
    {
        switch (style)
        {
            case BorderStyle.Flat:
                DrawFlat(spriteBatch, unitRectangle, flatColor, top, bottom, left, right, alphaMultiplier);
                break;

            case BorderStyle.FlatContrast:
                DrawFlatContrast(spriteBatch, unitRectangle, top, bottom, left, right, alphaMultiplier);
                break;

            case BorderStyle.Inset:
                DrawInset(spriteBatch, unitRectangle, top, bottom, left, right, alphaMultiplier);
                break;

            case BorderStyle.Outset:
                DrawOutset(spriteBatch, unitRectangle, top, bottom, left, right, alphaMultiplier);
                break;
        }
    }

    private static void DrawFlat(SpriteBatch spriteBatch, Texture2D unitRectangle, Color flatColor, Rectangle top, Rectangle bottom, Rectangle left, Rectangle right, float alphaMultiplier)
    {
        var borderColor = flatColor * alphaMultiplier;
        spriteBatch.Draw(unitRectangle, top, borderColor);
        spriteBatch.Draw(unitRectangle, bottom, borderColor);
        spriteBatch.Draw(unitRectangle, left, borderColor);
        spriteBatch.Draw(unitRectangle, right, borderColor);
    }

    /// <summary>Raised look: light catches the top-left as if lit from above, dark shadows the bottom-right.</summary>
    private static void DrawOutset(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle top, Rectangle bottom, Rectangle left, Rectangle right, float alphaMultiplier)
    {
        var light = LightBevelColor * alphaMultiplier;
        var dark = DarkBevelColor * alphaMultiplier;
        spriteBatch.Draw(unitRectangle, top, light);
        spriteBatch.Draw(unitRectangle, left, light);
        spriteBatch.Draw(unitRectangle, bottom, dark);
        spriteBatch.Draw(unitRectangle, right, dark);
    }

    /// <summary>Pressed look: same lighting as Outset, reversed -- top-left now in shadow, bottom-right catching light.</summary>
    private static void DrawInset(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle top, Rectangle bottom, Rectangle left, Rectangle right, float alphaMultiplier)
    {
        var dark = DarkBevelColor * alphaMultiplier;
        var light = LightBevelColor * alphaMultiplier;
        spriteBatch.Draw(unitRectangle, top, dark);
        spriteBatch.Draw(unitRectangle, left, dark);
        spriteBatch.Draw(unitRectangle, bottom, light);
        spriteBatch.Draw(unitRectangle, right, light);
    }

    /// <summary>
    /// A black ring around a light-grey ring -- each edge rect (drawn inward from the outer
    /// bounds, see BorderThickness.GetEdgeRectangles) splits in half along its thin axis: the
    /// half nearer the outside of bounds is black, the half nearer the content is light grey.
    /// Assumes an even thickness (2px is this style's whole point) -- an odd thickness still
    /// renders, just with the inner half getting the extra pixel rather than throwing.
    /// </summary>
    private static void DrawFlatContrast(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle top, Rectangle bottom, Rectangle left, Rectangle right, float alphaMultiplier)
    {
        var outer = ContrastOuterColor * alphaMultiplier;
        var inner = ContrastInnerColor * alphaMultiplier;

        var topOuterHeight = top.Height / 2;
        spriteBatch.Draw(unitRectangle, new Rectangle(top.X, top.Y, top.Width, topOuterHeight), outer);
        spriteBatch.Draw(unitRectangle, new Rectangle(top.X, top.Y + topOuterHeight, top.Width, top.Height - topOuterHeight), inner);

        var bottomInnerHeight = bottom.Height / 2;
        spriteBatch.Draw(unitRectangle, new Rectangle(bottom.X, bottom.Y, bottom.Width, bottomInnerHeight), inner);
        spriteBatch.Draw(unitRectangle, new Rectangle(bottom.X, bottom.Y + bottomInnerHeight, bottom.Width, bottom.Height - bottomInnerHeight), outer);

        var leftOuterWidth = left.Width / 2;
        spriteBatch.Draw(unitRectangle, new Rectangle(left.X, left.Y, leftOuterWidth, left.Height), outer);
        spriteBatch.Draw(unitRectangle, new Rectangle(left.X + leftOuterWidth, left.Y, left.Width - leftOuterWidth, left.Height), inner);

        var rightInnerWidth = right.Width / 2;
        spriteBatch.Draw(unitRectangle, new Rectangle(right.X, right.Y, rightInnerWidth, right.Height), inner);
        spriteBatch.Draw(unitRectangle, new Rectangle(right.X + rightInnerWidth, right.Y, right.Width - rightInnerWidth, right.Height), outer);
    }
}