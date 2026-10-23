using FontStashSharp;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Looting;

/// <summary>
/// A small, plain identity icon for one entity -- how the map draws it (its sprite, else its glyph),
/// drawn via the same SpriteOrGlyphRenderer primitive InventoryItemStackCell/MapWindow already
/// use, at full color (no dead-tint -- this is a portrait in a UI window, not the map tile
/// itself). Distinct from InventoryItemStackCell, which draws an item's icon, not an entity's.
/// </summary>
public sealed class EntityIconElement(
    FontService fontService,
    ElementPoolService elementPoolService,
    UiRenderService renderService,
    SpriteSheetService spriteSheetService,
    IMapViewQuery mapView)
    : Element(fontService, elementPoolService, renderService)
{
    private int _entityId;
    private MapVisual? _fixedVisual;
    private FontStashTextMeasurer _glyphFontMeasurer = null!;

    public void Configure(int entityId, Vector2 iconSize)
    {
        _entityId = entityId;
        _fixedVisual = null;
        _glyphFontMeasurer = FontService.GetTextMeasurer(FontService.GetFont((int)(iconSize.Y * GameFontChrome.IconGlyphFontFraction)));
    }

    /// <summary>Shows a visual that belongs to no entity -- a terrain or structure cell's, as one picture.</summary>
    public void Configure(TerrainVisualView visual, Vector2 iconSize)
    {
        _entityId = -1;
        _fixedVisual = spriteSheetService.Sprites.Resolve(visual);
        _glyphFontMeasurer = FontService.GetTextMeasurer(FontService.GetFont((int)(iconSize.Y * GameFontChrome.IconGlyphFontFraction)));
    }

    public override void DrawContent(GameTime gameTime)
    {
        if (_fixedVisual is { } visual)
        {
            SpriteOrGlyphRenderer.Draw(Render.Batch, spriteSheetService, visual.Sprite, _glyphFontMeasurer, visual.Glyph, visual.GlyphColor, ContentAbsolutePosition, ContentSize, Color.White);
            return;
        }

        if (mapView.TryGetVisual(_entityId, out var entityView))
        {
            var entityVisual = spriteSheetService.Sprites.Resolve(entityView);
            SpriteOrGlyphRenderer.Draw(Render.Batch, spriteSheetService, entityVisual.Sprite, _glyphFontMeasurer, entityVisual.Glyph, entityVisual.GlyphColor, ContentAbsolutePosition, ContentSize, Color.White);
        }
    }
}
