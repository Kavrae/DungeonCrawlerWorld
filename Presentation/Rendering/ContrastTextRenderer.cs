using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>
/// Generic high-contrast text draw primitive -- a full 1px outline (8 offset copies, not
/// just a single drop-shadow corner) behind a fill, legible against any background. Same
/// visual language as BorderStyle.FlatContrast (light content, black surround). Used for
/// HotbarContent's potion-cooldown countdown, PlayerStatusEffectsContent's own countdown and
/// status-effect stack counts, HotbarContent's per-slot bind-key/mana-cost/stack-count
/// overlays, and LabelRenderer (every glyph draw -- entities, map node badges, HUD/button
/// text -- so glyph text stays legible over whatever's rendered beneath it).
/// </summary>
public static class ContrastTextRenderer
{
    public static readonly Color FillColor = Color.White;
    public static readonly Color OutlineColor = Color.Black;

    private static readonly Vector2[] OutlineOffsets =
    [
        new(-1, -1), new(0, -1), new(1, -1),
        new(-1, 0), new(1, 0),
        new(-1, 1), new(0, 1), new(1, 1),
    ];

    /// <summary>position is the text's own top-left -- callers differ only in where they place it (HotbarContent centers above/within a slot, PlayerStatusEffectsContent centers below an icon). alphaMultiplier defaults to fully opaque -- HotbarContent's disabled-slot treatment passes a lower value to fade both the outline and the fill together. fillColor defaults to FillColor (white) -- LabelRenderer passes its caller-supplied glyph color instead, since glyph fill color is meaningful (race/terrain tinting) and only the outline needs to stay fixed black.</summary>
    public static void Draw(SpriteBatch spriteBatch, SpriteFontBase font, string text, Vector2 position, float alphaMultiplier = 1f, Color? fillColor = null)
    {
        var outline = OutlineColor * alphaMultiplier;
        foreach (var offset in OutlineOffsets)
        {
            spriteBatch.DrawString(font, text, position + offset, outline);
        }

        spriteBatch.DrawString(font, text, position, (fillColor ?? FillColor) * alphaMultiplier);
    }
}
