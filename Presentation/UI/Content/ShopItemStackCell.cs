using Engine.Utilities;
using Game.Blueprints;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;

namespace Presentation.UI.Content;

/// <summary>
/// The shop-mode item cell -- sprite (left, a CellSize.Y square) + truncated item name (top-right)
/// + price (bottom-right, "{total}G ({perItem} each)" for a stack, plain "{price}G" for a single
/// unit) instead of InventoryItemStackCell's own icon+quantity-badge layout. Same grid controls/
/// hover/selection/eligibility-glow as the normal grid -- DrawStateOverlay/GlowRenderer are reused
/// unchanged, only the icon-and-quantity portion of DrawContent is replaced. Named for what it's
/// for (shop buy/sell pricing), not "detailed," which would read as easily confusable with the
/// separate Item Details window. Configure (inherited) still drives everything but the price/name
/// text -- SetItemName/SetPrice are the two extra setters InventoryGridContent calls right after it
/// when building a shop-mode cell.
/// </summary>
public sealed class ShopItemStackCell(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer, SpriteSheetService spriteSheetService, SpriteRenderer spriteRenderer)
    : InventoryItemStackCell(fontService, elementPoolService, labelRenderer, spriteSheetService, spriteRenderer)
{
    private const float TextPadding = 4f;

    /// <summary>The name line's own top inset, distinct from TextPadding (the sprite-to-text gap) -- without it the name sat flush against the cell's own top edge, overlapping the eligible-glow ring drawn there (confirmed live; bumped once more after 3px still wasn't enough clearance).</summary>
    private const float NameTopPadding = 6f;

    /// <summary>Halfway between _quantityFont's own 0.5 fraction (see FontChrome.InventoryStackQuantityFontFraction, the original, too-large price size) and this cell's first attempt at shrinking it (0.25, confirmed too small live) -- 0.375.</summary>
    private static readonly float PriceFontSizeFraction = FontChrome.InventoryStackQuantityFontFraction * 0.75f;

    private string _itemName = string.Empty;
    private int _totalPrice;
    private int _perItemPrice;
    private FontStashTextMeasurer? _nameFontMeasurer;

    /// <summary>Set right after Configure -- Configure has no notion of an item's display Name (it only ever carried SpriteName/Glyph), so this is the one extra piece of data this subclass needs beyond what the base setter already captures.</summary>
    public void SetItemName(string name)
    {
        _itemName = name;
        _nameFontMeasurer = new FontStashTextMeasurer(_badgeFont);
    }

    /// <summary>totalPrice == perItemPrice (a single-unit stack) shows just "{price}G" -- the "(N each)" parenthetical is redundant when total and per-item are the same number.</summary>
    public void SetPrice(int totalPrice, int perItemPrice)
    {
        _totalPrice = totalPrice;
        _perItemPrice = perItemPrice;
    }

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
        var spriteSize = new Vector2(ContentSize.Y, ContentSize.Y);

        SpriteComponent? sprite = _spriteName is not null && SpriteManifest.TryGet(_spriteName, out var spriteComponent) ? spriteComponent : null;
        var spriteTint = isGreyedOut ? Color.Gray : Color.White;
        var glyphColor = isGreyedOut ? Color.Gray : _glyphColor;
        SpriteOrGlyphRenderer.Draw(spriteBatch, spriteSheetService, spriteRenderer, LabelRenderer, sprite, _iconGlyphFont, _glyph, glyphColor, ContentAbsolutePosition, spriteSize, spriteTint);

        var textColor = isGreyedOut ? Color.Gray : Color.White;
        var textLeft = ContentAbsolutePosition.X + spriteSize.X + TextPadding;
        var textWidth = System.Math.Max(0f, ContentSize.X - spriteSize.X - TextPadding);
        var halfHeight = ContentSize.Y / 2f;

        var truncatedName = _nameFontMeasurer is { } measurer ? StringUtility.TruncateWithEllipsis(measurer, _itemName, textWidth) : _itemName;
        LabelRenderer.DrawLeftAligned(spriteBatch, _badgeFont, truncatedName, new Vector2(textLeft, ContentAbsolutePosition.Y + NameTopPadding), new Vector2(textWidth, halfHeight - NameTopPadding), textColor);

        var priceFont = fontService.GetFont((int)(ContentSize.Y * PriceFontSizeFraction));
        var priceText = _totalPrice == _perItemPrice ? $"{_totalPrice}G" : $"{_totalPrice}G ({_perItemPrice} each)";
        LabelRenderer.DrawLeftAligned(spriteBatch, priceFont, priceText, new Vector2(textLeft, ContentAbsolutePosition.Y + halfHeight), new Vector2(textWidth, halfHeight), textColor);
    }
}
