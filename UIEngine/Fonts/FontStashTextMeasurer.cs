using Engine.Utilities;
using FontStashSharp;
using System.Text;

namespace UIEngine.Fonts;

/// <summary>Adapts a FontStashSharp font to Engine.Utilities' ITextMeasurer, so StringUtility's
/// word-wrap algorithms stay free of any rendering-library dependency.</summary>
/// <remarks>
/// FontStashSharp measures a string or a StringBuilder but not a span, so a span is copied into one reused
/// StringBuilder and measured there -- no allocation per measurement. One instance per font, from
/// FontService.GetTextMeasurer; not thread-safe, like the font itself.
/// </remarks>
public sealed class FontStashTextMeasurer(SpriteFontBase font) : ITextMeasurer
{
    private readonly StringBuilder _measuredText = new();

    public float MeasureWidth(ReadOnlySpan<char> text)
    {
        _measuredText.Clear().Append(text);
        return font.MeasureString(_measuredText).X;
    }
}
