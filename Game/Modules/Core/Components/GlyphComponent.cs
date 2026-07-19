using Microsoft.Xna.Framework;

namespace Game.Modules.Core.Components;

/// <summary>How an entity is displayed on the map.</summary>
public struct GlyphComponent(string glyph, Color glyphColor, Point glyphOffset)
{
    /// <summary>The characters drawn to the screen for this entity.</summary>
    public string Glyph { get; set; } = glyph;

    public Color GlyphColor { get; set; } = glyphColor;

    /// <summary>Pixel offset from the tile's origin, correcting unusual glyph positioning or multi-tile entities.</summary>
    public Point GlyphOffset { get; set; } = glyphOffset;

    public override readonly string ToString() => $"Glyph : {Glyph}";
}
