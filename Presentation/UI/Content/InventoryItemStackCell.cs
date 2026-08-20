using FontStashSharp;
using Game.Blueprints;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.Content;

/// <summary>
/// One square in the inventory grid: an item stack's sprite-else-glyph icon, plus its quantity
/// in the bottom-right corner when greater than 1. A plain Element (not Window) -- no title/
/// chrome needed, same reasoning Folder/Button use. IsDisabled gray-tints the icon, mirroring
/// Folder's own disabled tint and MapWindow's dead-entity tint (all three now share the same
/// SpriteOrGlyphRenderer draw primitive). ItemDefinitionId is exposed publicly so
/// UiInputController can read it directly (no Element-level drag hook needed -- see its own
/// content-drag state machine) when a press starts a drag from this cell toward a hotbar slot.
/// </summary>
public sealed class InventoryItemStackCell(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer, SpriteSheetService spriteSheetService, SpriteRenderer spriteRenderer)
    : Element(fontService, elementPoolService, glyphRenderer)
{
    private const float IconGlyphFontFraction = 0.6f;
    private const float QuantityFontFraction = 0.5f;

    private string? _spriteName;
    private string _glyph = string.Empty;
    private Color _glyphColor;
    private int _quantity;
    private bool _isDisabled;
    private SpriteFontBase _iconGlyphFont = null!;
    private SpriteFontBase _quantityFont = null!;

    public Guid ItemDefinitionId { get; private set; }

    /// <summary>Drives a translucent highlight overlay -- see InventoryGridContent's own hover polling. Mirrors AbilityScoreColumnHeader.IsHovered.</summary>
    public bool IsHovered { get; set; }

    /// <summary>cellSize is the caller's known fixed cell size (see InventoryGridContent), not ContentSize -- Configure runs immediately after CreateElement, before this cell's own layout has necessarily settled.</summary>
    public void Configure(Guid itemDefinitionId, string? spriteName, string glyph, Color glyphColor, int quantity, bool isDisabled, Vector2 cellSize)
    {
        ItemDefinitionId = itemDefinitionId;
        _spriteName = spriteName;
        _glyph = glyph;
        _glyphColor = glyphColor;
        _quantity = quantity;
        _isDisabled = isDisabled;
        _iconGlyphFont = fontService.GetFont((int)(cellSize.Y * IconGlyphFontFraction));
        _quantityFont = fontService.GetFont((int)(cellSize.Y * QuantityFontFraction));
    }

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = ElementPoolService.SpriteBatch;
        var unitRectangle = ElementPoolService.UnitRectangle;

        if (IsHovered)
        {
            spriteBatch.Draw(unitRectangle, new Rectangle((int)ContentAbsolutePosition.X, (int)ContentAbsolutePosition.Y, (int)ContentSize.X, (int)ContentSize.Y), WindowPalette.HighlightColor);
        }

        SpriteComponent? sprite = _spriteName is not null && SpriteManifest.TryGet(_spriteName, out var spriteComponent) ? spriteComponent : null;
        var spriteTint = _isDisabled ? Color.Gray : Color.White;
        var glyphColor = _isDisabled ? Color.Gray : _glyphColor;

        SpriteOrGlyphRenderer.Draw(spriteBatch, spriteSheetService, spriteRenderer, GlyphRenderer, sprite, _iconGlyphFont, _glyph, glyphColor, ContentAbsolutePosition, ContentSize, spriteTint);

        ItemIconRenderer.DrawQuantityBadge(spriteBatch, _quantityFont, _quantity, ContentAbsolutePosition, ContentSize);
    }
}
