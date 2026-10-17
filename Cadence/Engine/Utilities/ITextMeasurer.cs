namespace Engine.Utilities;

/// <summary>Represents a service for measuring the width of text in pixels.</summary>
/// <remarks>
/// Abstracts pixel-width text measurement so the word-wrap/truncate algorithms in
/// <see cref="StringUtility"/> stay pure and Engine-owned, with no dependency on a
/// specific rendering/font library. UIEngine provides the real implementation
/// (FontStashTextMeasurer, wrapping FontStashSharp's SpriteFontBase). Takes a span, so a caller
/// measures part of a string -- a word, a prefix, a line -- without slicing out a new one.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface ITextMeasurer
{
    float MeasureWidth(ReadOnlySpan<char> text);
}
