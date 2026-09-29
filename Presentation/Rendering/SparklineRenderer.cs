using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>Draws a series of values as a small graph filling a rectangle, one pixel column per slice of the series.</summary>
/// <remarks>
/// A column covering several values draws their largest, so a one-frame spike is never averaged
/// away when a long series is squeezed into a narrow graph.
/// </remarks>
public static class SparklineRenderer
{
    /// <param name="values">The series, oldest first; drawn right-aligned, so a series shorter than the graph is wide starts partway in.</param>
    /// <param name="minimumValue">The value at the graph's bottom edge.</param>
    /// <param name="maximumValue">The value at the graph's top edge; a value above it is drawn at the top.</param>
    /// <param name="style">Bars rise from the bottom edge; Line draws each column's step from the previous column's value.</param>
    /// <param name="referenceValue">Draws a 1 px horizontal line at this value when it is inside the graph's range, e.g. a frame budget.</param>
    public static void Draw(
        SpriteBatch spriteBatch,
        Texture2D unitRectangle,
        Rectangle area,
        ReadOnlySpan<double> values,
        double minimumValue,
        double maximumValue,
        SparklineStyle style,
        Color seriesColor,
        Color backgroundColor,
        double? referenceValue = null,
        Color referenceColor = default)
    {
        spriteBatch.Draw(unitRectangle, area, backgroundColor);
        if (values.IsEmpty || area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        var valueRange = System.Math.Max(maximumValue - minimumValue, double.Epsilon);
        var columnCount = System.Math.Min(area.Width, values.Length);
        var firstColumnX = area.Right - columnCount;
        var previousY = -1;

        for (var column = 0; column < columnCount; column++)
        {
            var firstValueIndex = (int)((long)column * values.Length / columnCount);
            var endValueIndex = (int)((long)(column + 1) * values.Length / columnCount);
            var columnValue = values[firstValueIndex];
            for (var valueIndex = firstValueIndex + 1; valueIndex < endValueIndex; valueIndex++)
            {
                columnValue = System.Math.Max(columnValue, values[valueIndex]);
            }

            var y = YOf(area, columnValue, minimumValue, valueRange);
            var x = firstColumnX + column;
            if (style is SparklineStyle.Bars)
            {
                if (y < area.Bottom)
                {
                    spriteBatch.Draw(unitRectangle, new Rectangle(x, y, 1, area.Bottom - y), seriesColor);
                }
            }
            else
            {
                var top = previousY < 0 ? y : System.Math.Min(previousY, y);
                var bottom = previousY < 0 ? y : System.Math.Max(previousY, y);
                spriteBatch.Draw(unitRectangle, new Rectangle(x, top, 1, bottom - top + 1), seriesColor);
                previousY = y;
            }
        }

        if (referenceValue is { } reference && reference >= minimumValue && reference <= maximumValue)
        {
            spriteBatch.Draw(unitRectangle, new Rectangle(area.X, YOf(area, reference, minimumValue, valueRange), area.Width, 1), referenceColor);
        }
    }

    private static int YOf(Rectangle area, double value, double minimumValue, double valueRange)
    {
        var fraction = System.Math.Clamp((value - minimumValue) / valueRange, 0, 1);
        return System.Math.Min(area.Bottom - 1, area.Bottom - 1 - (int)System.Math.Round(fraction * (area.Height - 1)));
    }
}
