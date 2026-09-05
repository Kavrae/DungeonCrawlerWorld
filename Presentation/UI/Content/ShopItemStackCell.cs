using Engine.Utilities;
using Game.Blueprints;
using Game.Modules.Core.Components;
using Game.Modules.Shops;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;

namespace Presentation.UI.Content;

/// <summary>
/// The shop-mode item cell -- sprite (left, a CellSize.Y square) + truncated item name (top-right)
/// + a stack-count/total-cost row (bottom-right, quantity flush left, "{total}G" flush right)
/// instead of InventoryItemStackCell's own icon+quantity-badge layout. The per-unit price and any
/// band breakdown live in the hover tooltip's own band table/receipt now (see InventoryGridContent.
/// ComputeHoverRows) -- this row only needs to answer "how many, for how much total," so it no
/// longer repeats the per-unit figure the tooltip already shows. Same grid controls/hover/selection/
/// eligibility-glow as the normal grid -- DrawStateOverlay/GlowRenderer are reused unchanged, only
/// the icon-and-quantity portion of DrawContent is replaced. Named for what it's for (shop buy/sell
/// pricing), not "detailed," which would read as easily confusable with the separate Item Details
/// window. Configure (inherited) still drives everything but the price/name text -- SetItemName/
/// SetPrice/SetStockStatus are the extra setters InventoryGridContent calls right after it when
/// building a shop-mode cell. Not sealed -- TradeItemStackCell subclasses this for the trade
/// window's own grids (small-square icon+quantity+price layout instead of this class's own wide
/// name+price row), reusing Configure/SetPrice/SetStockStatus/the favorable-price coloring via its
/// own DrawContent override. See that class's own doc comment.
/// </summary>
public class ShopItemStackCell(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer, SpriteSheetService spriteSheetService, SpriteRenderer spriteRenderer)
    : InventoryItemStackCell(fontService, elementPoolService, labelRenderer, spriteSheetService, spriteRenderer)
{
    private const float TextPadding = 4f;

    /// <summary>The name line's own top inset, distinct from TextPadding (the sprite-to-text gap) -- without it the name sat flush against the cell's own top edge, overlapping the eligible-glow ring drawn there (confirmed live; bumped once more after 3px still wasn't enough clearance).</summary>
    private const float NameTopPadding = 6f;

    /// <summary>
    /// Halfway between _quantityFont's own 0.5 fraction (see FontChrome.InventoryStackQuantityFontFraction,
    /// the original, too-large price size) and this cell's first attempt at shrinking it (0.25,
    /// confirmed too small live) -- 0.375. Named for what it's for generically, not "price"
    /// specifically -- protected, not private, so TradeItemStackCell's own DrawContent can reuse
    /// this same smaller size for its *quantity* text (confirmed live that _quantityFont, correctly
    /// sized for the plain inventory cell's lone quantity badge, is too large once that cell's own
    /// small square needs to fit a 3-digit-or-more number legibly).
    /// </summary>
    protected static readonly float CompactStatFontSizeFraction = FontChrome.InventoryStackQuantityFontFraction * 0.75f;

    /// <summary>Same Better/Worse pair ItemDetailsWindow already uses -- reused here so a favorable price and an unfavorable one read with the same color language elsewhere in the UI. See PLAN-stock-based-shop-pricing.md and PriceIsFavorable/PriceIsUnfavorable's own doc comment for which StockStatus counts as which, per grid. protected, not private -- TradeItemStackCell's own small-square layout reuses this same color pair for its price text.</summary>
    protected static readonly Color FavorableColor = Color.LightGreen;

    /// <summary>LightCoral, not IndianRed -- see ItemDetailsWindow.WorseColor's own doc comment for why (confirmed live too dark/muted here as well, same near-black panel background).</summary>
    protected static readonly Color UnfavorableColor = Color.LightCoral;

    private string _itemName = string.Empty;

    /// <summary>protected, not private -- TradeItemStackCell's own DrawContent override reads these directly (its small-square quantity/price layout replaces this class's own wide name+price row entirely, but reuses the same SetPrice-populated data).</summary>
    protected int _totalPrice;

    protected int _quantity;
    private FontStashTextMeasurer? _nameFontMeasurer;

    /// <summary>Set right after Configure -- Configure has no notion of an item's display Name (it only ever carried SpriteName/Glyph), so this is the one extra piece of data this subclass needs beyond what the base setter already captures.</summary>
    public void SetItemName(string name)
    {
        _itemName = name;
        _nameFontMeasurer = new FontStashTextMeasurer(_badgeFont);
    }

    public void SetPrice(int totalPrice, int quantity)
    {
        _totalPrice = totalPrice;
        _quantity = quantity;
    }

    /// <summary>Public for testability, same reasoning as StockStatus below -- what SetPrice last set, the real bulk-priced total for this cell's whole stack (see InventoryGridContent.ComputeShopTotalPrice).</summary>
    public int TotalPrice => _totalPrice;

    /// <summary>Mirrors CompareState's own public-for-testability shape (see InventoryItemStackCell) -- what SetStockStatus below last set. See ShopStockPricing.GetStockStatus -- always the shop's own status regardless of which grid this cell belongs to.</summary>
    public StockStatus StockStatus { get; private set; }

    /// <summary>True on the shop's own grid (isThisGridTheShop, see InventoryGridContent.ComputeShopPrices' own doc comment for that direction rule), false on the player's own grid while shop mode is active.</summary>
    public bool IsThisGridTheShop { get; private set; }

    /// <summary>
    /// A green price line: any band on the Overstocked/Flooded side (cheap) on the shop's own grid
    /// -- a good deal buying -- or any band on the Understocked/Desperate side (pricey) on the
    /// player's own grid -- a good deal selling. Reads StockStatus's own signed severity
    /// (Desperate/Flooded are the two extremes) rather than enumerating each band, so this needs no
    /// change as bands are added/removed. The opposite side on each grid instead reads
    /// PriceIsUnfavorable (red); Normal (0) is neither.
    /// </summary>
    public bool PriceIsFavorable => IsThisGridTheShop ? (int)StockStatus > 0 : (int)StockStatus < 0;

    /// <summary>See PriceIsFavorable's own doc comment -- the opposite side for this grid's own buy/sell direction.</summary>
    public bool PriceIsUnfavorable => IsThisGridTheShop ? (int)StockStatus < 0 : (int)StockStatus > 0;

    public void SetStockStatus(StockStatus status, bool isThisGridTheShop)
    {
        StockStatus = status;
        IsThisGridTheShop = isThisGridTheShop;
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

        var priceColor = isGreyedOut ? Color.Gray : PriceIsFavorable ? FavorableColor : PriceIsUnfavorable ? UnfavorableColor : textColor;

        var priceFont = fontService.GetFont((int)(ContentSize.Y * CompactStatFontSizeFraction));
        var priceRowPosition = new Vector2(textLeft, ContentAbsolutePosition.Y + halfHeight);
        var priceRowSize = new Vector2(textWidth, halfHeight);

        if (_quantity > 1)
        {
            LabelRenderer.DrawLeftAligned(spriteBatch, priceFont, _quantity.ToString(), priceRowPosition, priceRowSize, textColor);
        }

        // Inset from the cell's own right edge by the eligibility glow's own width -- flush against
        // priceRowSize (== bounds' own right edge), the total would sit directly under the
        // CompareState.Eligible InteriorFade glow's brightest, innermost ring (see GlowRenderer.
        // FadeRingCount's own doc comment; Tooltip.DrawContent's band-table price column needed the
        // same fix for the same reason).
        var totalPriceRowSize = new Vector2(System.Math.Max(0f, priceRowSize.X - GlowRenderer.FadeRingCount), priceRowSize.Y);
        LabelRenderer.DrawRightAligned(spriteBatch, priceFont, $"{_totalPrice}G", priceRowPosition, totalPriceRowSize, priceColor);
    }
}
