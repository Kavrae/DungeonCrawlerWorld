using Game.Views;
using UIEngine.Rendering;

namespace Presentation.Rendering;

/// <summary>Game's map views resolved for drawing through the loaded sprite manifest.</summary>
public static class MapVisualResolution
{
    /// <summary>visual resolved for drawing: its sprite name and seed to a cell, keeping its glyph for when there is none.</summary>
    public static MapVisual Resolve(this SpriteManifestResolver sprites, EntityVisualView visual)
    {
        SpriteView? sprite = visual.SpriteName is { } name && sprites.TryGetForEntity(name, visual.SpriteSeed, out var picked) ? picked : null;
        return new MapVisual(sprite, visual.Glyph, visual.GlyphColor, visual.IsDead);
    }

    /// <summary>A cell's terrain or structure as one picture -- a floor's option, a wall face's fallback piece -- for the inspector, which shows it alone rather than joined to its neighbours.</summary>
    public static MapVisual Resolve(this SpriteManifestResolver sprites, TerrainVisualView visual) =>
        visual.SpriteName is { } name && sprites.TryGetTerrainSprites(name, out var terrainSprites) && terrainSprites.TryGetFace(visual.Seed, mask: null, out var face)
            ? new MapVisual(face, string.Empty, default)
            : new MapVisual(null, visual.Glyph, visual.GlyphColor);
}
