using Microsoft.Xna.Framework;

namespace Game.Modules.Core.Components;

/// <summary>The glyph to be displayed when a sprite is not available.</summary>
public struct GlyphComponent(string glyph, Color glyphColor)
{
    /// <summary>The characters drawn to the screen for this entity.</summary>
    public string Glyph { get; set; } = glyph;

    /// <summary>The color of the glyph.</summary>
    public Color GlyphColor { get; set; } = glyphColor;

    public override readonly string ToString() => $"Glyph : {Glyph}\nGlyphColor : {GlyphColor}";
}