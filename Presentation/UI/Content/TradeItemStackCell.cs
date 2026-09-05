using Game.Blueprints;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI.Content;

/// <summary>
/// The trade window's own item cell -- a plain CellSize square (same footprint as
/// InventoryItemStackCell, not ShopItemStackCell's wide name+price rectangle) with just the
/// stack's quantity in the bottom-left corner, shadowed over the sprite the same way
/// InventoryItemStackCell's own quantity badge already is. No item name, no price at all (was
/// price + quantity both shown at first, but confirmed live that even the smaller
/// CompactStatFontSizeFraction reads cramped once a 3-digit-or-more quantity has to share the row
/// with a price string) -- the player already saw the price once, while dragging the item in from
/// their own inventory, and can still see it again on hover (ShopStockPricing.ComputeHoverRows
/// applies to every cell in this grid uniformly, price included, regardless of what's drawn inline
/// here), so it isn't needed a second time just to glance at what's currently offered. See
/// PLAN-trade-window.md's own case for why the trade grid is a third, distinct level of detail from
/// the other two: the inventory grid is a glance-and-sort view (sprite + count, no price), the shop
/// grid is a methodical browse-and-compare view (sprite + name + price), and the trade grid is a
/// glance-at-what's-offered view (sprite + count only) -- each cut down to exactly what its own use
/// case needs, not a single "detailed" cell reused everywhere. Extends ShopItemStackCell, not
/// InventoryItemStackCell directly, purely to reuse SetPrice/SetStockStatus (still called by
/// InventoryGridContent.RebuildCells the same way every ShopItemStackCell-family cell is, even
/// though this one no longer draws either) without redeclaring them -- DrawContent replaces
/// ShopItemStackCell's own wide layout entirely, the same way that class's own DrawContent already
/// replaces InventoryItemStackCell's.
/// </summary>
public sealed class TradeItemStackCell(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer, SpriteSheetService spriteSheetService, SpriteRenderer spriteRenderer)
    : ShopItemStackCell(fontService, elementPoolService, labelRenderer, spriteSheetService, spriteRenderer)
{
    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = ElementPoolService.SpriteBatch;
        var unitRectangle = ElementPoolService.UnitRectangle;

        var bounds = new Rectangle((int)ContentAbsolutePosition.X, (int)ContentAbsolutePosition.Y, (int)ContentSize.X, (int)ContentSize.Y);

        GridSquareRenderer.DrawBase(spriteBatch, unitRectangle, bounds);
        GridSquareRenderer.DrawStateOverlay(spriteBatch, unitRectangle, bounds, IsSelected ? GridSquareState.Selected : IsHovered ? GridSquareState.Hovered : GridSquareState.Normal);

        if (CompareState == CellCompareState.Eligible)
        {
            GlowRenderer.Draw(spriteBatch, unitRectangle, bounds, CompareEligibleGlowColor, GlowMode.ExteriorFade);
            GlowRenderer.Draw(spriteBatch, unitRectangle, bounds, CompareEligibleGlowColor, GlowMode.InteriorFade);
        }

        var isGreyedOut = _isDisabled || CompareState == CellCompareState.Ineligible;

        SpriteComponent? sprite = _spriteName is not null && SpriteManifest.TryGet(_spriteName, out var spriteComponent) ? spriteComponent : null;
        var spriteTint = isGreyedOut ? Color.Gray : Color.White;
        var glyphColor = isGreyedOut ? Color.Gray : _glyphColor;
        SpriteOrGlyphRenderer.Draw(spriteBatch, spriteSheetService, spriteRenderer, LabelRenderer, sprite, _iconGlyphFont, _glyph, glyphColor, ContentAbsolutePosition, ContentSize, spriteTint);

        if (_quantity <= 0)
        {
            return;
        }

        // CompactStatFontSizeFraction (ShopItemStackCell's own smaller price-text size), not
        // _quantityFont -- confirmed live that _quantityFont's own size, correct for the plain
        // inventory cell's lone quantity badge, is too large once this cell's own small square
        // needs to fit a 3-digit-or-more quantity legibly.
        var quantityFont = fontService.GetFont((int)(ContentSize.Y * CompactStatFontSizeFraction));
        ItemIconRenderer.DrawBottomAligned(spriteBatch, quantityFont, _quantity.ToString(), ContentAbsolutePosition, ContentSize, alignRight: false, Color.White);
    }
}
