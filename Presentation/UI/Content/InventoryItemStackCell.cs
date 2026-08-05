using FontStashSharp;
using Game.Blueprints;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI.Content;

/// <summary>
/// One square in the inventory grid: an item stack's sprite-else-glyph icon, plus its quantity
/// in the bottom-right corner when greater than 1. A plain Element (not Window) -- no title/
/// chrome needed, same reasoning Folder/Button use. IsDisabled gray-tints the icon, mirroring
/// Folder's own disabled tint and MapWindow's dead-entity tint (all three now share the same
/// SpriteOrGlyphRenderer draw primitive).
/// </summary>
public sealed class InventoryItemStackCell(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer, SpriteSheetService spriteSheetService, SpriteRenderer spriteRenderer)
    : Element(fontService, elementPoolService, glyphRenderer)
{
    private const float IconGlyphFontFraction = 0.6f;
    private const float QuantityFontFraction = 0.45f;
    private static readonly Color QuantityShadowColor = Color.Black;
    private static readonly Color QuantityTextColor = Color.White;
    private static readonly Vector2 QuantityShadowOffset = new(-1, -1);
    private static readonly Vector2 QuantityTextPadding = new(2, 0);

    private string? _spriteName;
    private string _glyph = string.Empty;
    private Color _glyphColor;
    private int _quantity;
    private bool _isDisabled;
    private SpriteFontBase _iconGlyphFont = null!;
    private SpriteFontBase _quantityFont = null!;

    /// <summary>cellSize is the caller's known fixed cell size (see InventoryGridContent), not ContentSize -- Configure runs immediately after CreateElement, before this cell's own layout has necessarily settled.</summary>
    public void Configure(string? spriteName, string glyph, Color glyphColor, int quantity, bool isDisabled, Vector2 cellSize)
    {
        _spriteName = spriteName;
        _glyph = glyph;
        _glyphColor = glyphColor;
        _quantity = quantity;
        _isDisabled = isDisabled;
        _iconGlyphFont = fontService.GetFont((int)(cellSize.Y * IconGlyphFontFraction));
        _quantityFont = fontService.GetFont((int)(cellSize.Y * QuantityFontFraction));
    }

    public override void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        SpriteComponent? sprite = _spriteName is not null && SpriteManifest.TryGet(_spriteName, out var spriteComponent) ? spriteComponent : null;
        var spriteTint = _isDisabled ? Color.Gray : Color.White;
        var glyphColor = _isDisabled ? Color.Gray : _glyphColor;

        SpriteOrGlyphRenderer.Draw(spriteBatch, spriteSheetService, spriteRenderer, GlyphRenderer, sprite, _iconGlyphFont, _glyph, glyphColor, ContentAbsolutePosition, ContentSize, spriteTint);

        if (_quantity > 1)
        {
            var text = _quantity.ToString();
            var textSize = _quantityFont.MeasureString(text);
            var textPosition = ContentAbsolutePosition + ContentSize - textSize - QuantityTextPadding;
            spriteBatch.DrawString(_quantityFont, text, textPosition, QuantityShadowColor);
            spriteBatch.DrawString(_quantityFont, text, textPosition + QuantityShadowOffset, QuantityTextColor);
        }
    }
}
