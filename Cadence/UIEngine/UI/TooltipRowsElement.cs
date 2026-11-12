using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.UI.Chrome;

namespace UIEngine.UI;

/// <summary>A block of TooltipRow lines on its own, one font line each -- a table inside a window rather than under a tooltip's text.</summary>
/// <remarks>Sized by its caller: HeightFor gives the height a set of rows needs. Configure measures; drawing measures nothing.</remarks>
public sealed class TooltipRowsElement(ElementServices services)
    : Element(services)
{
    private readonly TooltipRowBlock _rowBlock = new();
    private SpriteFontBase _font = null!;
    private GlowPulseClock _rowGlowPulse;

    /// <summary>The height rows take in the default font.</summary>
    public static float HeightFor(FontService fontService, IReadOnlyList<TooltipRow> rows) =>
        fontService.GetFont(FontChrome.DefaultFontSize).LineHeight * rows.Count;

    /// <summary>Shows rows, copied so a caller refilling its own list changes nothing shown.</summary>
    public void Configure(IReadOnlyList<TooltipRow> rows)
    {
        _font = FontService.GetFont(FontChrome.DefaultFontSize);
        _rowBlock.Set(_font, rows);
        _rowGlowPulse.Restart();
    }

    public override void DrawContent(GameTime gameTime)
    {
        if (_rowBlock.Count == 0)
        {
            return;
        }

        _rowGlowPulse.StartPendingAt(gameTime.TotalGameTime);
        _rowBlock.Draw(Render.Batch, _font, Vector2.Zero, LocalContentRectangle.Width, _rowGlowPulse.ElapsedAt(gameTime.TotalGameTime));
    }
}
