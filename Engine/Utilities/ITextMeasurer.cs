namespace Engine.Utilities;

/// <summary>Represents a service for measuring the width of text in pixels.</summary>
/// <remarks>
/// Abstracts pixel-width text measurement so the word-wrap/truncate algorithms in
/// <see cref="StringUtility"/> stay pure and Engine-owned, with no dependency on a
/// specific rendering/font library. Presentation provides the real implementation
/// (wrapping FontStashSharp's SpriteFontBase).
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface ITextMeasurer
{
    float MeasureWidth(string text);
}