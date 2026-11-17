using Microsoft.Xna.Framework;

namespace UIEngine.UI;

/// <summary>A small coloured glyph drawn before a label -- a menu row's or a chip's.</summary>
public readonly record struct GlyphIcon(string Glyph, Color Color)
{
    /// <summary>Draws nothing and takes its place: a row without an icon in a list where others have one, so the labels line up.</summary>
    public static readonly GlyphIcon Blank = new(string.Empty, Color.Transparent);
}
