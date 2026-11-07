using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;

namespace UIEngine.UI;

/// <summary>A block of TooltipRow lines on its own, one font line each -- a table inside a window rather than under a tooltip's text.</summary>
/// <remarks>Sized by its caller: HeightFor gives the height a set of rows needs. Configure measures; drawing measures nothing.</remarks>
public sealed class TooltipRowsElement(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
    : Element(fontService, elementPoolService, renderService)
{
    private readonly List<TooltipRow> _rows = [];
    private readonly List<float> _rightTextWidths = [];
    private SpriteFontBase _font = null!;
    private float _middleColumnLeft;

    /// <summary>The height rows take in the default font.</summary>
    public static float HeightFor(FontService fontService, IReadOnlyList<TooltipRow> rows) =>
        fontService.GetFont(FontChrome.DefaultFontSize).LineHeight * rows.Count;

    /// <summary>Shows rows, copied so a caller refilling its own list changes nothing shown.</summary>
    public void Configure(IReadOnlyList<TooltipRow> rows)
    {
        _font = FontService.GetFont(FontChrome.DefaultFontSize);
        _rows.Clear();
        for (var index = 0; index < rows.Count; index++)
        {
            _rows.Add(rows[index]);
        }

        _middleColumnLeft = TooltipRowDrawing.MeasureMiddleColumnLeft(_font, _rows);
        TooltipRowDrawing.MeasureRightTextWidths(_font, _rows, _rightTextWidths);
    }

    public override void DrawContent(GameTime gameTime)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        TooltipRowDrawing.Draw(Render.Batch, Render.UnitRectangle, _font, _rows, _rightTextWidths, _middleColumnLeft, Vector2.Zero, LocalContentRectangle.Width);
    }
}
