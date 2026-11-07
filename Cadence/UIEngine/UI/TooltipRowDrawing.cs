using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UIEngine.Rendering;

namespace UIEngine.UI;

/// <summary>Measures and draws a block of TooltipRow lines -- a Tooltip's rows under its body text, or a TooltipRowsElement on its own.</summary>
/// <remarks>Measuring is separate from drawing so a caller measures once per change of rows, never per draw.</remarks>
public static class TooltipRowDrawing
{
    /// <summary>Gap between the middle column's shared start and the widest LeftText before it, so a range never sits flush against its label.</summary>
    private const float MiddleColumnGap = 6f;

    /// <summary>Where every row's MiddleText starts: past the widest LeftText among rows that have one, plus a gap -- 0 when none has one.</summary>
    public static float MeasureMiddleColumnLeft(SpriteFontBase font, IReadOnlyList<TooltipRow> rows)
    {
        var widestLeftText = 0f;
        var anyMiddleText = false;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.IsDivider || string.IsNullOrEmpty(row.MiddleText))
            {
                continue;
            }

            anyMiddleText = true;
            widestLeftText = System.Math.Max(widestLeftText, font.MeasureString(row.LeftText).X);
        }

        return anyMiddleText ? widestLeftText + MiddleColumnGap : 0f;
    }

    /// <summary>Replaces rightTextWidths' contents with each row's RightText width, parallel to rows.</summary>
    public static void MeasureRightTextWidths(SpriteFontBase font, IReadOnlyList<TooltipRow> rows, List<float> rightTextWidths)
    {
        rightTextWidths.Clear();
        for (var index = 0; index < rows.Count; index++)
        {
            var rightText = rows[index].RightText;
            rightTextWidths.Add(string.IsNullOrEmpty(rightText) ? 0f : font.MeasureString(rightText).X);
        }
    }

    /// <summary>Draws rows one line each from topLeft down, rowWidth wide: a rule for a divider, otherwise the left, middle and right columns, and an inner glow around a row that has one.</summary>
    /// <remarks>
    /// RightText is inset from the row's right edge by the glow's width, on every row so the column lines up, so it never sits under the glow's brightest ring.
    /// A null elapsedSinceRowGlowPulseStart draws every row's glow steady, whatever its GlowAnimation.
    /// </remarks>
    public static void Draw<TTarget>(TTarget target, Texture2D unitRectangle, SpriteFontBase font, IReadOnlyList<TooltipRow> rows, IReadOnlyList<float> rightTextWidths, float middleColumnLeft, Vector2 topLeft, float rowWidth, TimeSpan? elapsedSinceRowGlowPulseStart)
        where TTarget : ISpriteDrawTarget
    {
        var rowSize = new Vector2(rowWidth, font.LineHeight);
        var rightColumnSize = new Vector2(System.Math.Max(0f, rowSize.X - GlowRenderer.FadeRingCount), rowSize.Y);

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var rowPosition = topLeft + new Vector2(0, font.LineHeight * rowIndex);
            var rowBounds = new Rectangle((int)rowPosition.X, (int)rowPosition.Y, (int)rowSize.X, (int)rowSize.Y);

            if (row.IsDivider)
            {
                var lineRectangle = new Rectangle(rowBounds.X, rowBounds.Y + rowBounds.Height / 2, rowBounds.Width, 1);
                target.Draw(unitRectangle, lineRectangle, row.Color);
                continue;
            }

            LabelRenderer.DrawLeftAligned(target, font, row.LeftText, rowPosition, rowSize, row.Color);
            if (!string.IsNullOrEmpty(row.MiddleText))
            {
                LabelRenderer.DrawLeftAligned(target, font, row.MiddleText, rowPosition + new Vector2(middleColumnLeft, 0), rowSize, row.Color);
            }

            if (!string.IsNullOrEmpty(row.RightText))
            {
                LabelRenderer.DrawRightAligned(target, font, row.RightText, rightTextWidths[rowIndex], rowPosition, rightColumnSize, row.Color);
            }

            if (row.GlowColor is not { } glowColor)
            {
                continue;
            }

            if (elapsedSinceRowGlowPulseStart is { } elapsed)
            {
                GlowRenderer.Draw(target, unitRectangle, rowBounds, glowColor, GlowMode.InteriorFade, row.GlowAnimation, elapsed);
                continue;
            }

            GlowRenderer.Draw(target, unitRectangle, rowBounds, glowColor, GlowMode.InteriorFade);
        }
    }
}
