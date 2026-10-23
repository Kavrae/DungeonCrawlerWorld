using FontStashSharp;
using Microsoft.Xna.Framework;

using Presentation.UI.Chrome;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Content;

/// <summary>
/// A small, plain icon for one item -- its own sprite if it has one, else its glyph, drawn via
/// the same SpriteOrGlyphRenderer primitive InventoryItemStackCell/EntityIconElement already use.
/// Unlike InventoryItemStackCell, carries no quantity badge, hover/selection glow, or group
/// border -- just the bare icon, for a details/summary context where a full grid cell's worth of
/// per-stack chrome doesn't apply (see ItemDetailsWindow). Unlike EntityIconElement, takes its
/// sprite/glyph directly rather than resolving them from an entity's own component pools -- an
/// item has no entity id of its own to look up.
/// </summary>
public sealed class ItemIconElement(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService, SpriteSheetService spriteSheetService)
    : Element(fontService, elementPoolService, renderService)
{
    private SpriteView? _sprite;
    private string _glyph = string.Empty;
    private Color _glyphColor;
    private FontStashTextMeasurer _glyphFontMeasurer = null!;

    public void Configure(string? spriteName, string glyph, Color glyphColor, Vector2 iconSize)
    {
        _sprite = spriteSheetService.Sprites.FirstOrNull(spriteName);
        _glyph = glyph;
        _glyphColor = glyphColor;
        _glyphFontMeasurer = FontService.GetTextMeasurer(FontService.GetFont((int)(iconSize.Y * GameFontChrome.IconGlyphFontFraction)));
    }

    public override void DrawContent(GameTime gameTime)
    {
        SpriteOrGlyphRenderer.Draw(Render.Batch, spriteSheetService, _sprite, _glyphFontMeasurer, _glyph, _glyphColor, ContentAbsolutePosition, ContentSize, Color.White);
    }
}
