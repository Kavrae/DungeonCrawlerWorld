using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Input.DragDrop;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using UIEngine;
using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Content;

/// <summary>Item Details Comparison's own per-cell state while armed (see MapViewState.CompareRequiredActivatorType/InventoryGridContent's own per-frame sync) -- None outside compare mode entirely.</summary>
public enum CellCompareState
{
    None,
    Eligible,
    Ineligible,
}

/// <summary>
/// One square in the inventory grid: an item stack's sprite-else-glyph icon, plus its quantity
/// in the bottom-left corner when greater than 1 -- bottom-left, not bottom-right, for consistency
/// with ShopItemStackCell/TradeItemStackCell's own quantity placement, both of which reserve the
/// bottom-right corner for price. A plain Element (not Window) -- no title/
/// chrome needed, same reasoning Folder/Button use. IsDisabled gray-tints the icon, mirroring
/// Folder's own disabled tint and MapWindow's dead-entity tint (all three now share the same
/// SpriteOrGlyphRenderer draw primitive). A drag source (TryBeginContentDrag): a press picks up
/// its stack, or a Merged Stack's shared item, as a DragPayload.
///
/// Once per-slot item divergence exists (see InventoryGridContent's own GroupDivergedStacks),
/// this cell shows one of three things -- terms this codebase uses consistently for all three:
/// a **Base item stack** (StackInstanceId set, IsDivergent false -- an ordinary, undiverged
/// stack), a **Diverging item stack** shown on its own (StackInstanceId set, IsDivergent true --
/// its own Override differs from the catalog original, e.g. a wand's own remaining charges), or
/// a **Merged Stack**: a display-only cell standing in for two or more stacks (any mix of Base/
/// Diverging) that share one ItemDefinitionId (StackInstanceId null -- there is no single stack a
/// click/drag against this cell could mean; MergedStackBadgeVisible true whenever at least one
/// member is Diverging). Clicking a Merged Stack's badge expands it into its own **Expansion Stacks** --
/// the same underlying Base/Diverging cells it was standing in for, now shown individually (see
/// InventoryGridContent.OnCellClicked/RebuildCells). CanBindToHotbar is false only for the Merged
/// Stack case -- a Diverging item stack (whether standalone or currently shown as one of a Merged
/// Stack's Expansion Stacks) is exactly as bindable as a Base one (see that property's own doc
/// comment for why). A Merged Stack can still be dragged (see TryBeginContentDrag) -- just never dropped onto a hotbar slot -- so a future manual-sort gesture has something
/// to build on; it's the one case that gets the disabled-cursor treatment while hovering a hotbar
/// slot mid-drag.
/// </summary>
/// <summary>Not sealed -- ShopItemStackCell subclasses this for the shop-mode grid (sprite+name+price layout instead of icon+quantity-badge), reusing Configure/the base fill+hover/selected/eligible-glow drawing via its own DrawContent override. See that class's own doc comment.</summary>
public class InventoryItemStackCell(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService, SpriteSheetService spriteSheetService)
    : Element(fontService, elementPoolService, renderService), IContentDragSource
{
    private static readonly Color GroupBorderColor = Color.Black;
    private const float GroupBorderThickness = 2f;

    /// <summary>Item Details Comparison's own "eligible to add" glow -- distinct from WindowPalette.AttentionGlow (Gold, the grid-square selected/armed color); also the same color a comparison column's own advantage-highlighted lines use (ItemDetailsWindow.BetterColor), so "green" reads consistently as "good" everywhere this feature touches. Also reused as-is by ShopItemStackCell's own eligible-glow (shop trade eligibility, not comparison, but the same "green means you can" visual language).</summary>
    protected static readonly Color CompareEligibleGlowColor = Color.LightGreen;

    /// <summary>protected, not private -- ShopItemStackCell's own DrawContent override reads these directly (its sprite/name/price layout replaces the icon+quantity-badge one below entirely, so it can't reuse this class's own DrawContent body, only its Configure-computed data).</summary>
    private SpriteView? _sprite;
    protected string _glyph = string.Empty;
    protected Color _glyphColor;
    private int _quantity;
    protected bool _isDisabled;
    private bool _itemCanBindToHotbar;
    private bool _groupBorderTop;
    private bool _groupBorderBottom;
    private bool _groupBorderLeft;
    private bool _groupBorderRight;
    protected FontStashTextMeasurer _iconGlyphFontMeasurer = null!;
    protected SpriteFontBase _quantityFont = null!;
    protected SpriteFontBase _badgeFont = null!;

    private const string MergedStackBadgeText = "+";

    /// <summary>MergedStackBadgeText's width in _badgeFont, measured when the font is set, not per draw.</summary>
    private float _mergedStackBadgeWidth;

    /// <summary>The quantity badge's text, rebuilt only when the quantity changes.</summary>
    private readonly KeyedCache<int, string> _quantityBadgeText = new();

    private readonly TextSizeCache _quantityBadgeTextSize = new();

    public Guid ItemDefinitionId { get; private set; }

    /// <summary>The entity whose inventory this cell belongs to -- what UiInputController's content-drag path reads as a transfer's origin entity when dropped onto a different entity's own grid (see InventoryActions.TryTransferStack). Every inventory grid has exactly one owning entity, so this is the same value for every cell InventoryGridContent builds.</summary>
    public int EntityId { get; private set; }

    /// <summary>The exact stack this cell represents -- what UiInputController's content-drag path reads to bind a hotbar slot to one specific physical stack, not just "some stack of this item id" (see ItemHotkeyBindingComponent's own doc comment). Null for a merged group cell (see this class's own doc comment) -- there is no single stack to bind.</summary>
    public uint? StackInstanceId { get; private set; }

    /// <summary>True when this cell shows one single divergent stack on its own (not merged into a group) -- see this class's own doc comment.</summary>
    public bool IsDivergent { get; private set; }

    /// <summary>True when this cell is a merged group containing at least one divergent member -- drives the "+" Merged Stack badge, the signal a click should expand it (see InventoryGridContent.RebuildCells).</summary>
    public bool MergedStackBadgeVisible { get; private set; }

    /// <summary>
    /// False for a merged group cell (StackInstanceId null -- there is no single stack a
    /// drop onto a hotbar slot could mean), and for an item that can never be bound at all
    /// (ItemHotkeyBindingQueries.CanBind -- a loot box). A single divergent stack -- whether it never needed
    /// merging in the first place, or is currently shown as one of a Merged Stack's Expansion
    /// Stacks after clicking to expand -- is exactly as bindable as a base stack: it already
    /// resolves to one exact physical StackInstanceId, the same identity ItemActivationSystem's
    /// own PeelWandCharge repoints a binding to automatically once a wand fires. Divergence itself
    /// was never the thing worth blocking -- ambiguity (which physical stack a merged cell's drag
    /// would even mean) is.
    /// </summary>
    public bool CanBindToHotbar => StackInstanceId is not null && _itemCanBindToHotbar;

    /// <summary>False for an item that never leaves its owner's inventory (ItemDefinition.CanTrade) -- no Give, Take, Sell, Buy or trade option is offered for it.</summary>
    public bool CanTrade { get; private set; }

    /// <summary>False for a cell that only displays an item and never starts a drag, such as a loot box reward; reset to true by every Configure.</summary>
    public bool IsDragSource { get; set; }

    /// <summary>Drives a translucent highlight overlay -- see InventoryGridContent's own hover polling. Mirrors AbilityScoreColumnHeader.IsHovered.</summary>
    public bool IsHovered { get; set; }

    /// <summary>Drives GridSquareRenderer's own gold inner+outer selected glow (see DrawStateOverlay, the same treatment HotbarContent's own ArmedSlot glow uses) -- true when this cell's StackInstanceId matches MapViewState.SelectedItemStackInstanceId, the item currently shown in the Item Details window. Set every frame alongside IsHovered -- see InventoryGridContent's own per-frame sync, not a rebuild-driven Configure parameter, since selection changes independently of any grid rebuild.</summary>
    public bool IsSelected { get; set; }

    /// <summary>Item Details Comparison's own per-frame state, set alongside IsSelected, independent of shop mode -- Eligible glows green (this item shares the anchor's Activator type, so clicking it would add it), Ineligible grey-tints the icon the same way IsDisabled already does, None (comparison not armed) draws neither. Comparison and shop-trade eligibility (ShopTradeEligible below) are computed and rendered independently -- a shop-ineligible item that's still comparison-eligible shows both cues at once (greyed icon, green comparison glow).</summary>
    public CellCompareState CompareState { get; set; }

    /// <summary>
    /// Shop mode only (true, the meaningless-outside-shop-mode default, otherwise), set alongside
    /// CompareState by InventoryGridContent.UpdateShopEligibilityState -- true when this item can be
    /// traded with the open shop (ShopActions.CanTrade tag match) AND whichever side would be paying
    /// Gold can currently afford it. False greys the icon out (see DrawBaseAndIcon) and blocks a
    /// direct drag/Give/Take/Sell All/Buy All (see InventoryTransferDropHandler.AllowsDragStart and
    /// BuildItemContextMenu's isShopIneligible) -- closes the currency-drain-style exploit a naive
    /// reuse of plain Give/Take would otherwise open for wrong-tag or unaffordable trades. Distinct
    /// from CanStageInTrade (tag match only, ignoring affordability, see its own doc comment) -- an
    /// unaffordable-but-tradeable item still reads ShopTradeEligible false (greyed out, still blocked
    /// from a direct buy/sell) but CanStageInTrade true, so it can still be dragged into the trade
    /// window to stage. Deliberately never drives the green eligible-glow (see DrawBaseAndIcon) --
    /// that glow is Item Details Comparison's own cue exclusively, so a shop-eligible item reads with
    /// no special highlight beyond its own price-line favorable/unfavorable coloring (ShopItemStackCell.
    /// PriceIsFavorable/PriceIsUnfavorable).
    /// </summary>
    public bool ShopTradeEligible { get; set; } = true;

    /// <summary>
    /// Shop mode only, set alongside ShopTradeEligible by InventoryGridContent.UpdateShopEligibilityState
    /// -- true whenever this item's tags actually match the open shop's own AcceptedItems
    /// (ShopActions.CanTrade), regardless of whether the paying side can currently afford it. An
    /// unaffordable-but-tradeable item still reads ShopTradeEligible false (greyed out, still blocked
    /// from a direct buy/sell -- ShopActions.TryBuyFromShop/TrySellToShop's own affordability check
    /// refuses it with no state changed either way) but CanStageInTrade true, so
    /// InventoryTransferDropHandler.AllowsDragStart/BuildItemContextMenu's "Add to trade" can still let it be picked up and
    /// staged in the trade window -- only a wrong-tag item (CanTrade false) or a Merged Stack (no
    /// single stack to trade) is ever fully blocked from that. False outside shop mode entirely.
    /// </summary>
    public bool CanStageInTrade { get; set; }

    /// <summary>True for a lit unit of a toggle item -- draws ToggleActiveMarker. Set after Configure, which resets it, by whatever built the cell; a pooled cell never carries it over.</summary>
    public bool IsToggledOn { get; set; }

    /// <summary>
    /// cellSize is the caller's known fixed cell size (see InventoryGridContent), not ContentSize
    /// -- Configure runs immediately after CreateElement, before this cell's own layout has
    /// necessarily settled. Unconditionally clears any group-border edges left over from a
    /// previous Configure -- ElementPoolService reuses cell instances, so a cell that was
    /// previously an expanded-group member must not still be drawing that border once it's
    /// reconfigured for an unrelated stack (confirmed bug: the border "leaked" onto other items
    /// this way). SetGroupBorderEdges, called separately afterward, is the only thing that turns
    /// any of them back on for this Configure's cell.
    /// </summary>
    public void Configure(int entityId, Guid itemDefinitionId, uint? stackInstanceId, string? spriteName, string glyph, Color glyphColor, int quantity, bool isDisabled, bool isDivergent, bool mergedStackBadgeVisible, bool canTrade, bool itemCanBindToHotbar, Vector2 cellSize)
    {
        CanTrade = canTrade;
        _itemCanBindToHotbar = itemCanBindToHotbar;
        EntityId = entityId;
        ItemDefinitionId = itemDefinitionId;
        StackInstanceId = stackInstanceId;
        IsDivergent = isDivergent;
        MergedStackBadgeVisible = mergedStackBadgeVisible;
        _sprite = spriteSheetService.Sprites.FirstOrNull(spriteName);
        _glyph = glyph;
        _glyphColor = glyphColor;
        IsDragSource = true;
        _quantity = quantity;
        _isDisabled = isDisabled;
        ShopTradeEligible = true;
        IsToggledOn = false;
        _groupBorderTop = false;
        _groupBorderBottom = false;
        _groupBorderLeft = false;
        _groupBorderRight = false;
        _iconGlyphFontMeasurer = FontService.GetTextMeasurer(FontService.GetFont((int)(cellSize.Y * GameFontChrome.InventoryStackIconGlyphFontFraction)));
        _quantityFont = FontService.GetFont((int)(cellSize.Y * GameFontChrome.InventoryStackQuantityFontFraction));
        _badgeFont = FontService.GetFont((int)(cellSize.Y * GameFontChrome.InventoryStackBadgeFontFraction));
        _mergedStackBadgeWidth = _badgeFont.MeasureString(MergedStackBadgeText).X;
    }

    /// <summary>
    /// Which of this cell's four edges sit on the outer perimeter of the currently-expanded
    /// group it belongs to -- an edge draws the group border only when true (its row-major
    /// neighbor on that side isn't also a member of the group, or there is no neighbor at all).
    /// Only ever called for an expanded group's own member cells (see InventoryGridContent.
    /// RebuildCells) -- every other cell stays at Configure's own all-false default, so this
    /// border never shows anywhere else.
    /// </summary>
    public void SetGroupBorderEdges(bool top, bool bottom, bool left, bool right)
    {
        _groupBorderTop = top;
        _groupBorderBottom = bottom;
        _groupBorderLeft = left;
        _groupBorderRight = right;
    }

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;
        var bounds = LocalContentRectangle;

        DrawBaseAndIcon(spriteBatch, unitRectangle, bounds, ContentSize);

        if (_quantity > 1)
        {
            var quantityText = _quantityBadgeText.Get(_quantity, static quantity => quantity.ToString());
            ItemIconRenderer.DrawQuantityBadge(spriteBatch, _quantityFont, quantityText, _quantityBadgeTextSize.SizeOf(_quantityFont, quantityText), Vector2.Zero, ContentSize);
        }

        if (MergedStackBadgeVisible)
        {
            var badgePosition = new Vector2(ContentSize.X - _mergedStackBadgeWidth, 0);
            ContrastTextRenderer.Draw(spriteBatch, _badgeFont, MergedStackBadgeText, badgePosition);
        }

        DrawGroupBorder(spriteBatch, unitRectangle);
    }

    /// <summary>
    /// Shared grid-square base + state overlay + compare-eligible glow + sprite-or-glyph icon draw
    /// every cell in this family starts with -- ShopItemStackCell/TradeItemStackCell's own
    /// DrawContent overrides call this too (with their own iconSize -- ShopItemStackCell's own
    /// left-aligned sprite square is smaller than the cell itself, unlike this class's own
    /// full-ContentSize icon), then draw only whatever comes after it that's actually unique to
    /// their own layout. Returns isGreyedOut so a caller's own trailing text (name/price/quantity)
    /// can match this same disabled/ineligible tint without recomputing the same condition itself.
    /// </summary>
    protected bool DrawBaseAndIcon(SpriteEffectBatch spriteBatch, Texture2D unitRectangle, Rectangle bounds, Vector2 iconSize)
    {
        GridSquareRenderer.DrawBase(spriteBatch, unitRectangle, bounds);
        GridSquareRenderer.DrawStateOverlay(spriteBatch, unitRectangle, bounds, IsSelected ? GridSquareState.Selected : IsHovered ? GridSquareState.Hovered : GridSquareState.Normal);

        if (CompareState == CellCompareState.Eligible)
        {
            GlowRenderer.Draw(spriteBatch, unitRectangle, bounds, CompareEligibleGlowColor, GlowMode.ExteriorFade);
            GlowRenderer.Draw(spriteBatch, unitRectangle, bounds, CompareEligibleGlowColor, GlowMode.InteriorFade);
        }

        var isGreyedOut = _isDisabled || CompareState == CellCompareState.Ineligible || !ShopTradeEligible;
        var spriteTint = isGreyedOut ? Color.Gray : Color.White;
        var glyphColor = isGreyedOut ? Color.Gray : _glyphColor;

        SpriteOrGlyphRenderer.Draw(spriteBatch, spriteSheetService, _sprite, _iconGlyphFontMeasurer, _glyph, glyphColor, Vector2.Zero, iconSize, spriteTint);

        if (IsToggledOn)
        {
            ToggleActiveMarker.Draw(spriteBatch, unitRectangle, bounds);
        }

        return isGreyedOut;
    }

    /// <summary>
    /// Replaces this cell's own normal border (see InventoryGridContent.RebuildCells, which
    /// passes ShowBorder: false for an expanded group's member cells) rather than drawing
    /// alongside it -- a solid black line only on the group's outer perimeter edges, so two
    /// adjacent member cells share a clean, unbroken join with no border between them at all.
    /// </summary>
    private void DrawGroupBorder(SpriteEffectBatch spriteBatch, Texture2D unitRectangle)
    {
        if (!_groupBorderTop && !_groupBorderBottom && !_groupBorderLeft && !_groupBorderRight)
        {
            return;
        }

        var bounds = LocalContentRectangle;
        var thickness = BorderThickness.Uniform(new Vector2(GroupBorderThickness, GroupBorderThickness));
        var (top, bottom, left, right) = BorderThickness.GetEdgeRectangles(bounds, thickness);

        BorderRenderer.Draw(
            spriteBatch, unitRectangle, BorderStyle.Flat, GroupBorderColor,
            _groupBorderTop ? top : Rectangle.Empty,
            _groupBorderBottom ? bottom : Rectangle.Empty,
            _groupBorderLeft ? left : Rectangle.Empty,
            _groupBorderRight ? right : Rectangle.Empty);
    }

    /// <summary>
    /// Picks up this cell's stack, or a Merged Stack's shared item (no single stack -- tracked so it can still move between
    /// inventories as a batch, refused only where it would bind to a hotbar slot). An item that can neither leave its
    /// owner's inventory nor bind to the hotbar (a loot box) has nowhere a drop could send it, and a display-only cell (a
    /// loot box reward) stands for an item rather than holding it -- neither starts a drag.
    /// </summary>
    /// <remarks>
    /// The ghost is square at the cell's smaller side whatever the cell's shape -- a ShopItemStackCell is 4x wider than
    /// tall, and the ghost draws only the item's icon, which stretched to the cell's width read as squashed (confirmed live).
    /// </remarks>
    public bool TryBeginContentDrag(Point pressPosition, out ContentDragStart start)
    {
        if (!IsDragSource || (!CanTrade && !CanBindToHotbar))
        {
            start = default;
            return false;
        }

        var payload = StackInstanceId is { } stackInstanceId
            ? new DragPayload(DragPayloadOrigin.InventoryCell, ItemStackInstanceId: stackInstanceId, OriginEntityId: EntityId, CanStageInTrade: CanStageInTrade)
            : new DragPayload(DragPayloadOrigin.InventoryCell, MergedItemDefinitionId: ItemDefinitionId, OriginEntityId: EntityId, CanStageInTrade: CanStageInTrade);
        var ghostDimension = System.Math.Min(CurrentSize.X, CurrentSize.Y);
        start = new ContentDragStart(payload, new Vector2(ghostDimension, ghostDimension));
        return true;
    }
}
