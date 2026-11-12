using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UIEngine.Rendering;

namespace UIEngine.UI;

/// <summary>A block of TooltipRows as shown: its own copy of the rows and what was measured from them when they were set.</summary>
/// <remarks>
/// The rows are copied, so a caller reusing and refilling its own list can't change what was laid out behind its back. The
/// middle column's start and each right text's width are measured once per Set, not per draw. The shared Tooltip and
/// TooltipRowsElement each hold one.
/// </remarks>
public sealed class TooltipRowBlock
{
    private readonly List<TooltipRow> _rows = [];
    private readonly List<float> _rightTextWidths = [];
    private float _middleColumnLeft;

    public int Count => _rows.Count;

    /// <summary>Whether rows are exactly the rows shown; null counts as none.</summary>
    public bool Shows(IReadOnlyList<TooltipRow>? rows)
    {
        if (_rows.Count != (rows?.Count ?? 0))
        {
            return false;
        }

        for (var index = 0; index < _rows.Count; index++)
        {
            if (_rows[index] != rows![index])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Shows rows, measured in font; null or empty shows none.</summary>
    public void Set(SpriteFontBase font, IReadOnlyList<TooltipRow>? rows)
    {
        _rows.Clear();
        if (rows is not null)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                _rows.Add(rows[index]);
            }
        }

        _middleColumnLeft = TooltipRowDrawing.MeasureMiddleColumnLeft(font, _rows);
        TooltipRowDrawing.MeasureRightTextWidths(font, _rows, _rightTextWidths);
    }

    /// <summary>Draws the rows from topLeft down, each rowWidth wide, in the font they were measured in.</summary>
    /// <param name="elapsedSinceRowGlowPulseStart">How long the rows' glows have been pulsing; null draws every glow steady at full strength.</param>
    public void Draw<TTarget>(TTarget target, SpriteFontBase font, Vector2 topLeft, float rowWidth, TimeSpan? elapsedSinceRowGlowPulseStart)
        where TTarget : ISpriteDrawTarget
    {
        if (_rows.Count == 0)
        {
            return;
        }

        TooltipRowDrawing.Draw(target, font, _rows, _rightTextWidths, _middleColumnLeft, topLeft, rowWidth, elapsedSinceRowGlowPulseStart);
    }
}
