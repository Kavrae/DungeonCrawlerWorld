using Microsoft.Xna.Framework;

namespace Game.Modules.Core.Components;

/// <summary>
/// How an entity is displayed on the map. Positioning (including centering within a
/// multi-tile footprint) is computed from the font's own measured glyph size at draw time --
/// see GlyphRenderer.GetCenteredPosition -- not stored here.
/// </summary>
public struct GlyphComponent(string glyph, Color glyphColor)
{
    /// <summary>The characters drawn to the screen for this entity.</summary>
    public string Glyph { get; set; } = glyph;

    public Color GlyphColor { get; set; } = glyphColor;

    public override readonly string ToString() => $"Glyph : {Glyph}";
}