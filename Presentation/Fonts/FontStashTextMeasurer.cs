using Engine.Utilities;
using FontStashSharp;

namespace Presentation.Fonts;

/// <summary>Adapts a FontStashSharp font to Engine.Utilities' ITextMeasurer, so StringUtility's
/// word-wrap algorithms stay free of any rendering-library dependency.</summary>
public sealed class FontStashTextMeasurer(SpriteFontBase font) : ITextMeasurer
{

    public float MeasureWidth(string text) => font.MeasureString(text).X;
}