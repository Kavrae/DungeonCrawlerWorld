namespace Engine.Utilities;

/// <summary>
/// Abstracts pixel-width text measurement so the word-wrap/truncate algorithms in
/// <see cref="StringUtility"/> stay pure and Engine-owned, with no dependency on a
/// specific rendering/font library. Presentation provides the real implementation
/// (wrapping FontStashSharp's SpriteFontBase).
/// </summary>
public interface ITextMeasurer
{
    float MeasureWidth(string text);
}
