using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>
/// Shared outline+fill+tick-mark rendering for any current/maximum resource bar -- health, mana,
/// and any other resource that follows the same shape. FractionBarElement wraps it as an element.
/// The one thing that differs per resource -- its palette (outline + fraction-to-color mapping) --
/// is the caller's own to supply, so this stays resource-agnostic. A caller that hides a full bar
/// decides that before calling Draw -- this only draws whatever rectangle/fraction it's given.
/// </summary>
public static class ResourceBarRenderer
{
    private static readonly Color NoResourceColor = Color.LightGray;
    private static readonly float[] MajorTickFractions = [0.25f, 0.5f, 0.75f];
    private static readonly float[] MinorTickFractions = [0.125f, 0.375f, 0.625f, 0.875f];

    /// <param name="fraction">Current/effective-maximum, clamped [0,1] by the caller.</param>
    /// <param name="hasResource">False draws NoResourceColor instead of fractionColor(fraction) -- e.g. "no SimpleHealthComponent at all," not merely "empty."</param>
    /// <param name="outlineColor">This resource's own outline/tick color -- see HealthBarPalette.OutlineColor/ManaBarPalette.OutlineColor.</param>
    /// <param name="fractionColor">This resource's own fraction-to-fill-color mapping -- see HealthBarPalette.FractionColor/ManaBarPalette.FractionColor.</param>
    public static void Draw<TTarget>(TTarget spriteBatch, Rectangle bar, float fraction, bool hasResource, Color outlineColor, Func<float, Color> fractionColor)
        where TTarget : ISpriteDrawTarget
    {
        spriteBatch.FillRectangle(bar, outlineColor);

        var fillColor = hasResource ? fractionColor(fraction) : NoResourceColor;

        var innerWidth = (int)((bar.Width - 2) * fraction);
        if (innerWidth > 0)
        {
            spriteBatch.FillRectangle(new Rectangle(bar.X + 1, bar.Y + 1, innerWidth, bar.Height - 2), fillColor);
        }

        DrawTicks(spriteBatch, bar, outlineColor);
    }

    /// <summary>Draws valueText centred on bar in outlined white, on top of whatever Draw put there.</summary>
    /// <remarks>Centred as a line (LabelRenderer.GetLineCenteredPosition), not on its ink.</remarks>
    /// <param name="valueTextWidth">valueText's measured width, from the caller's TextSizeCache.</param>
    public static void DrawCenteredValueText<TTarget>(TTarget spriteBatch, SpriteFontBase font, Rectangle bar, string valueText, float valueTextWidth)
        where TTarget : ISpriteDrawTarget
    {
        var position = LabelRenderer.GetLineCenteredPosition(font, valueTextWidth, new Vector2(bar.X, bar.Y), new Vector2(bar.Width, bar.Height));
        ContrastTextRenderer.Draw(spriteBatch, font, valueText, position);
    }

    /// <summary>Major ticks (half bar height) at the 1/4, 1/2, 3/4 marks; minor ticks (quarter bar height) at the 1/8, 3/8, 5/8, 7/8 marks -- both flush with the bar's bottom edge (ruler-style graduations), drawn over the fill.</summary>
    private static void DrawTicks<TTarget>(TTarget spriteBatch, Rectangle bar, Color outlineColor)
        where TTarget : ISpriteDrawTarget
    {
        foreach (var fraction in MajorTickFractions)
        {
            DrawTick(spriteBatch, bar, fraction, bar.Height / 2, outlineColor);
        }

        foreach (var fraction in MinorTickFractions)
        {
            DrawTick(spriteBatch, bar, fraction, bar.Height / 4, outlineColor);
        }
    }

    private static void DrawTick<TTarget>(TTarget spriteBatch, Rectangle bar, float widthFraction, int tickHeight, Color outlineColor)
        where TTarget : ISpriteDrawTarget
    {
        var tickX = bar.X + (int)(bar.Width * widthFraction);
        var tickY = bar.Bottom - tickHeight;

        spriteBatch.FillRectangle(new Rectangle(tickX, tickY, 1, tickHeight), outlineColor);
    }
}
