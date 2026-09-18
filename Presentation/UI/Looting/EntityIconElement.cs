using Engine.ECS.Components.Stores;
using FontStashSharp;
using Game.Modules.Core.Components;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;

namespace Presentation.UI.Looting;

/// <summary>
/// A small, plain identity icon for one entity -- its own sprite if it has one, else its glyph,
/// drawn via the same SpriteOrGlyphRenderer primitive InventoryItemStackCell/MapWindow already
/// use, at full color (no dead-tint -- this is a portrait in a UI window, not the map tile
/// itself). Distinct from InventoryItemStackCell, which draws an item's icon, not an entity's.
/// </summary>
public sealed class EntityIconElement(
    FontService fontService,
    ElementPoolService elementPoolService,
    LabelRenderer labelRenderer,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    DirectComponentPool<SpriteComponent> spritePool,
    DirectComponentPool<GlyphComponent> glyphPool)
    : Element(fontService, elementPoolService, labelRenderer)
{
    private int _entityId;
    private EntityVisualView? _fixedVisual;
    private SpriteFontBase _glyphFont = null!;

    public void Configure(int entityId, Vector2 iconSize)
    {
        _entityId = entityId;
        _fixedVisual = null;
        _glyphFont = fontService.GetFont((int)(iconSize.Y * FontChrome.IconGlyphFontFraction));
    }

    /// <summary>Shows a visual that belongs to no entity -- a terrain cell's.</summary>
    public void Configure(EntityVisualView visual, Vector2 iconSize)
    {
        _entityId = -1;
        _fixedVisual = visual;
        _glyphFont = fontService.GetFont((int)(iconSize.Y * FontChrome.IconGlyphFontFraction));
    }

    public override void DrawContent(GameTime gameTime)
    {
        if (_fixedVisual is { } visual)
        {
            SpriteOrGlyphRenderer.Draw(ElementPoolService.SpriteBatch, spriteSheetService, spriteRenderer, LabelRenderer, visual.Sprite, _glyphFont, visual.Glyph, visual.GlyphColor, ContentAbsolutePosition, ContentSize, Color.White);
            return;
        }

        SpriteComponent? sprite = spritePool.TryGetReadonly(_entityId, out var spriteComponent) ? spriteComponent : null;
        var hasGlyph = glyphPool.TryGetReadonly(_entityId, out var glyphComponent);
        var glyph = hasGlyph ? glyphComponent.Glyph : string.Empty;
        var glyphColor = hasGlyph ? glyphComponent.GlyphColor : Color.White;

        SpriteOrGlyphRenderer.Draw(ElementPoolService.SpriteBatch, spriteSheetService, spriteRenderer, LabelRenderer, sprite, _glyphFont, glyph, glyphColor, ContentAbsolutePosition, ContentSize, Color.White);
    }
}
