using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using FontStashSharp;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops;
using Game.Modules.Shops.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Content;

namespace Presentation.UI.Trade;

/// <summary>
/// The middle window of the three-window shop layout (PLAN-trade-window.md) -- two columns
/// (player-offered items on the left, shop-offered items on the right), each a fixed 20-slot,
/// non-scrolling item grid over its own reserved trade-offer entity plus a currency footer, under
/// a header showing that column's own running Value. A shared 3-button footer (Balance Offer,
/// Cancel, Complete) sits beneath both columns.
///
/// Drag-eligibility (Add to trade/Remove from trade/direct sell/direct buy, both items and
/// currency) and live header Value computation (ComputeColumnValueText) are landed -- see
/// PLAN-trade-window.md's own "Landed" notes throughout. Still to land, per TODO.md's "Trade
/// window" entry: Balance Offer/Complete's own logic (both buttons stay inert placeholders below)
/// and the whole-window drop-zone widening (drops still require landing exactly on a grid cell or
/// the currency row, not just anywhere in the column). Every position/size below is explicit, not
/// ambient-propagated -- same convention ShopWindow/SecondaryInventoryWindow already established
/// for this window family.
/// </summary>
public sealed class TradeWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    LabelRenderer labelRenderer,
    ComponentManager componentManager,
    ItemCatalog itemCatalog,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    World world,
    ContextMenuController contextMenuController,
    MapViewState mapViewState,
    // Null in test setups that don't wire one -- CompleteTrade's own player-to-shop Gold transfer
    // simply never publishes GoldGivenToShopEvent in that case (see CompleteTrade's own doc comment).
    EventBus? eventBus = null)
    : Window(fontService, elementPoolService, labelRenderer), IWholeWindowDropTarget
{
    /// <summary>2x10 -- the confirmed 20-stacks-per-side cap, arranged so every slot is visible with no scrolling required (see InventoryCapacity.MaxNonPlayerStackCount, which already enforces this same 20 for free -- see PLAN-trade-window.md's own "Trade grid capacity" section).</summary>
    private const int GridColumns = 2;

    private const int GridRows = 10;

    private static readonly Vector2 GridSize = new(
        GridColumns * (InventoryGridContent.CellSize.X + InventoryGridContent.CellGap) - InventoryGridContent.CellGap,
        GridRows * (InventoryGridContent.CellSize.Y + InventoryGridContent.CellGap) - InventoryGridContent.CellGap);

    private const float HeaderHeight = 32f;
    private const float HeaderLabelLineHeight = 16f;
    // +2px (confirmed live) for clearer separation between the player/shop columns, especially now
    // that the trade grid's own background is transparent -- there's no longer a panel-color edge
    // of its own to mark where one column's content ends and the gap begins. Shared by the header
    // labels/grid/currency footer alike (all three position off this same constant), not a
    // grid-only value, so everything in a column still lines up with everything else in it.
    private const float ColumnGap = 10f;
    private const float SectionGap = 4f;
    private const float ButtonHeight = 24f;

    private static readonly Vector2 ColumnSize = new(GridSize.X, GridSize.Y);

    /// <summary>Content width: two columns side by side plus the gap between them.</summary>
    private static readonly float ContentWidth = ColumnSize.X * 2 + ColumnGap;

    /// <summary>Content height: header + grid + currency footer for a column, then the Balance Offer row, then the Cancel/Complete row.</summary>
    private static readonly float ContentHeight = HeaderHeight + GridSize.Y + CurrencyRowContent.Height + SectionGap * 2 + ButtonHeight * 2;

    private int _playerSideEntityId;
    private int _shopSideEntityId;

    /// <summary>
    /// The *real* shop entity, captured once at Configure time -- not re-read from
    /// MapViewState.OpenShopEntityId later, since ShopWindowController.HandleClosed already clears
    /// that back to null *before* invoking TradeWindowController.CloseForShopClosed (see its own
    /// doc comment: "fired at the end of HandleClosed, after every other cleanup"), which is what
    /// eventually calls this window's own ReturnEverythingToOwners. Re-reading OpenShopEntityId at
    /// that point would already find it cleared, silently no-opping the unwind and permanently
    /// stranding whatever was staged in either trade-offer entity. A trade window's whole lifetime
    /// is scoped to exactly one shop session, so this captured id is stable for as long as this
    /// instance exists.
    /// </summary>
    private int _shopEntityId;

    private readonly PackedComponentPool<ShopComponent>? _shopPool = componentManager.IsRegistered<ShopComponent>() ? componentManager.GetPackedPool<ShopComponent>() : null;
    private readonly PackedComponentPool<CurrencyComponent>? _currencyPool = componentManager.IsRegistered<CurrencyComponent>() ? componentManager.GetPackedPool<CurrencyComponent>() : null;
    private readonly MultiComponentPool<InventoryItemStackComponent> _stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

    /// <summary>
    /// One Tooltip per column, not one shared between both -- see TradeWindowController.Initialize's
    /// own doc comment for the confirmed-live "whichever grid updates second stomps the other's
    /// ShowNear/Hide" bug this avoids (the same reason InventoryWindowController and
    /// AbilityScoreWindowController already keep their own hover popups separate).
    /// </summary>
    private Tooltip _playerColumnHoverPopup = null!;

    private Tooltip _shopColumnHoverPopup = null!;

    /// <summary>Recomputed every Update by ComputeColumnValueText -- "0G" only until the first Update ever runs, or while no shop is open. Public for testability, same reasoning as ShopItemStackCell.TotalPrice/StockStatus.</summary>
    public string PlayerValueText => _playerValueText;

    public string ShopValueText => _shopValueText;

    private string _playerValueText = "0G";

    private string _shopValueText = "0G";
    private Button _balanceOfferButton = null!;
    private Button _cancelButton = null!;
    private Button _completeButton = null!;

    /// <summary>
    /// Settable late-bound callback fired when the player clicks Cancel -- distinct from Closed
    /// (which fires for this too, since Cancel still ends by calling Close(), but also fires for
    /// the X button/Escape) so TradeWindowController can tell "the player explicitly abandoned
    /// this trade" apart from every other way this window closes, and run ReturnEverythingToOwners
    /// accordingly (every reason runs it except Complete, which already ran its own swap instead --
    /// see CloseReason's own doc comment). All three windows of the session close together
    /// regardless of which of the four ways this window closed.
    /// </summary>
    public Action? OnCancelClicked { get; set; }

    /// <summary>Settable late-bound callback fired after CompleteTrade has already swapped everything -- TradeWindowController uses this (not a direct Close() call here) so it can mark the resulting close as CloseReason.Complete, the one close path that must NOT also run ReturnEverythingToOwners (the swap already happened).</summary>
    public Action? OnCompleteClicked { get; set; }

    /// <summary>Must be called after CreateElement but before Initialize -- same contract ShopWindow.Configure follows.</summary>
    public void Configure(int playerSideEntityId, int shopSideEntityId, int shopEntityId, Tooltip playerColumnHoverPopup, Tooltip shopColumnHoverPopup)
    {
        _playerSideEntityId = playerSideEntityId;
        _shopSideEntityId = shopSideEntityId;
        _shopEntityId = shopEntityId;
        _playerColumnHoverPopup = playerColumnHoverPopup;
        _shopColumnHoverPopup = shopColumnHoverPopup;
    }

    /// <summary>
    /// IWholeWindowDropTarget -- unlike ShopWindow/InventoryManagementWindow (exactly one entity
    /// per whole window), this window hosts two, so a whole-window drop (one that missed both
    /// columns' own specific grid/currency child, e.g. landing on the header or a section gap)
    /// picks between them by which half of this window's own width dropPosition falls in: left of
    /// the midpoint is the player-side column, right of it the shop-side column -- matching the two
    /// columns' own fixed left/right layout (BuildColumn's own x=0 / x=ColumnSize.X+ColumnGap
    /// split) exactly. Item and currency resolve identically, since each column's own currency
    /// footer represents the same trade-offer entity as its own item grid.
    /// </summary>
    public int ResolveItemDropEntityId(Point dropPosition) => IsInPlayerHalf(dropPosition) ? _playerSideEntityId : _shopSideEntityId;

    /// <summary>See ResolveItemDropEntityId.</summary>
    public int ResolveCurrencyDropEntityId(Point dropPosition) => IsInPlayerHalf(dropPosition) ? _playerSideEntityId : _shopSideEntityId;

    private bool IsInPlayerHalf(Point dropPosition) => dropPosition.X < ContentAbsolutePosition.X + ContentSize.X / 2f;

    /// <summary>
    /// See ShopWindow.OnChildrenInitialized's own doc comment for why children are built here, not
    /// in Configure. Unlike ShopWindow/SecondaryInventoryWindow -- both created with their own
    /// final Layout.Size already known and passed at CreateElement time -- this window's own final
    /// size isn't known until here, so it's built with no Layout.Size at all (see
    /// TradeWindowController.Open). Element.Build's own MaximumSize fallback chain
    /// (Layout.MaximumSize ?? parent.ContentSize ?? Layout.Size ?? Vector2.Zero) resolves to a bare
    /// Vector2.Zero for a parentless window with none of those set -- SetMaximumSize here raises it
    /// to the real final size before SetSize, the same "AbilityScoreWindow column count" fix this
    /// codebase already needed once before (Element.Build only ever sets MaximumSize once; a later
    /// SetSize past a stale/wrong ceiling silently clamps back down to it otherwise).
    /// </summary>
    protected override void OnChildrenInitialized()
    {
        var outerInsets = CurrentSize - ContentSize;
        var finalSize = new Vector2(ContentWidth, ContentHeight) + outerInsets;
        SetMaximumSize(finalSize);
        SetMinimumSize(finalSize);
        SetSize(finalSize);

        base.OnChildrenInitialized();

        BuildColumn(0, _playerSideEntityId, isShopSide: false);
        BuildColumn(ColumnSize.X + ColumnGap, _shopSideEntityId, isShopSide: true);

        BuildFooterButtons();
    }

    /// <summary>
    /// Recomputes both header Values (and the two footer buttons' own Enabled state) every frame --
    /// the same "poll every Update, don't wire change events" convention every other per-frame UI
    /// read in this codebase already follows (cheap, at most 20 stacks per column). Must run after
    /// base.Update (which drives each column's own InventoryGridContent/CurrencyRowContent) so a
    /// drag resolved earlier this same frame is already reflected, though in practice a one-frame
    /// lag either way would be imperceptible.
    /// </summary>
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        var playerValue = ComputeColumnValue(_playerSideEntityId, isShopSide: false);
        var shopValue = ComputeColumnValue(_shopSideEntityId, isShopSide: true);
        _playerValueText = $"{playerValue}G";
        _shopValueText = $"{shopValue}G";

        var isEmpty = IsColumnEmpty(_playerSideEntityId) && IsColumnEmpty(_shopSideEntityId);
        _balanceOfferButton.Enabled = !isEmpty;
        _completeButton.Enabled = !isEmpty && playerValue >= shopValue;
    }

    /// <summary>No item stacks and no Gold/Credits of any kind currently sitting in tradeEntityId's own column -- both Complete and Balance Offer stay disabled while both columns read this (PLAN-trade-window.md's own "nothing offered on either side" rule).</summary>
    private bool IsColumnEmpty(int tradeEntityId)
    {
        if (_stacks.GetFirstDenseIndex(tradeEntityId) != -1)
        {
            return false;
        }

        return _currencyPool is null || !_currencyPool.TryGetReadonly(tradeEntityId, out var currency) || (currency.Gold == 0 && currency.Credits == 0);
    }

    /// <summary>
    /// Sum of every item stack's *current* trade price in tradeEntityId's own column (via the exact
    /// same ShopStockPricing.ComputeBulkSellPrice/ComputeBulkBuyPrice calls the grid cells
    /// themselves already use to price a cell, and InventoryGridContent.GetEffectiveShopStock for
    /// the identical "a stack staged in the shop-side column still counts as the shop's own stock"
    /// correction those cells already need -- see its own doc comment) plus that column's own
    /// footer Gold. 0 whenever the shop's own ShopComponent can't be resolved at all (shouldn't
    /// normally arise once _shopEntityId is captured, but mirrors InventoryGridContent's own
    /// fallback for a missing/destroyed shop).
    ///
    /// Every stack is priced independently against the shop's *actual* current stock, not a running
    /// simulation of "what would stock be if every other item already in this trade had already
    /// been sold/bought" -- real stock only actually changes at Complete, when items physically
    /// move. Same-item stacks are grouped and priced together in one bulk-price call *before*
    /// summing, not each priced independently then summed -- bulk pricing is a non-linear,
    /// band-crossing curve (ShopStockPricing.ComputeBulkPrice), so five separate 1-unit calls would
    /// each price against the *same* starting stock level instead of the cumulative depletion/
    /// buildup five units bought/sold together actually causes, overstating this column's real Value.
    /// </summary>
    private int ComputeColumnValue(int tradeEntityId, bool isShopSide)
    {
        if (_shopPool is null || !_shopPool.TryGetReadonly(_shopEntityId, out var shop))
        {
            return 0;
        }

        var groupedQuantities = new Dictionary<Guid, (ItemDefinition Definition, int Quantity)>();
        for (var denseIndex = _stacks.GetFirstDenseIndex(tradeEntityId); denseIndex != -1; denseIndex = _stacks.GetNextDenseIndex(denseIndex))
        {
            var stack = _stacks.GetReadonlyByDenseIndex(denseIndex);
            if (!InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var definition))
            {
                continue;
            }

            groupedQuantities[definition.Id] = groupedQuantities.TryGetValue(definition.Id, out var existing)
                ? (definition, existing.Quantity + stack.Quantity)
                : (definition, stack.Quantity);
        }

        var total = 0;
        foreach (var (definition, quantity) in groupedQuantities.Values)
        {
            var effectiveStock = InventoryGridContent.GetEffectiveShopStock(componentManager, mapViewState, _shopEntityId, definition.Id);
            var preferredStockLevel = ShopStockPricing.GetPreferredStockLevel(componentManager, _shopEntityId, definition.Id);
            total += isShopSide
                ? ShopStockPricing.ComputeBulkBuyPrice(effectiveStock, preferredStockLevel, shop, definition, (ushort)quantity)
                : ShopStockPricing.ComputeBulkSellPrice(effectiveStock, preferredStockLevel, shop, definition, (ushort)quantity);
        }

        if (_currencyPool?.TryGetReadonly(tradeEntityId, out var currency) == true)
        {
            total += currency.Gold;
        }

        return total;
    }

    /// <summary>
    /// Moves every item stack currently in sourceEntityId's own inventory to destinationEntityId,
    /// one InventoryActions.TryTransferStack call per stack (the same primitive every other
    /// transfer in this feature already uses) -- what both CompleteTrade (swap) and
    /// ReturnEverythingToOwners (unwind) are built from. Copies the stack list first rather than
    /// walking _stacks' own dense chain directly, since TryTransferStack removes from that same
    /// chain mid-walk -- the same "collect then act" shape InventoryActions.TryTransferAllStacksOfItem
    /// already uses for an identical reason.
    /// </summary>
    private void TransferAllStacksTo(int sourceEntityId, int destinationEntityId)
    {
        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(_stacks, sourceEntityId, stacks);
        foreach (var stack in stacks)
        {
            InventoryActions.TryTransferStack(componentManager, sourceEntityId, destinationEntityId, stack.StackInstanceId, world);
        }
    }

    /// <summary>
    /// Cancel, closing the shop window, and closing the player inventory window all run this same
    /// unwind (see TradeWindowController.HandleWindowClosed) -- return everything to where it
    /// started, the mirror image of CompleteTrade's swap. Public so the controller (which owns the
    /// actual Close() sequencing and CloseReason bookkeeping) can call it at the right moment,
    /// before this window is actually returned to the pool.
    /// </summary>
    public void ReturnEverythingToOwners()
    {
        TransferAllStacksTo(_playerSideEntityId, world.PlayerEntityId);
        TransferAllStacksTo(_shopSideEntityId, _shopEntityId);
        CurrencyActions.TryTransfer(componentManager, _playerSideEntityId, world.PlayerEntityId, CurrencyType.Gold);
        CurrencyActions.TryTransfer(componentManager, _shopSideEntityId, _shopEntityId, CurrencyType.Gold);
    }

    /// <summary>
    /// A direct item/currency swap between the two trade entities and the two real entities -- not
    /// routed through ShopActions.TryBuyFromShop/TrySellToShop, which do their own per-call
    /// bulk-pricing-and-charge; pricing already did its job gating _completeButton.Enabled, so this
    /// only needs to move what's physically sitting in each column. Because this moves real
    /// InventoryItemStackComponent stacks into/out of the real shop entity, the shop's stock bands
    /// for the *next* trade or purchase already reflect the change for free -- ShopStockPricing.
    /// GetTotalStock reads live state, no special post-trade recompute needed.
    ///
    /// The player-to-shop Gold leg is routed through ShopActions.TryGiveCurrencyToShop, not a plain
    /// CurrencyActions.TryTransfer, so completing a trade that moves player Gold to the shop --
    /// including a trade offering only Gold and no items at all -- publishes GoldGivenToShopEvent
    /// the same as every other "give currency to a shop" gesture (confirmed live gap: this path
    /// never published it before).
    /// </summary>
    private void CompleteTrade()
    {
        TransferAllStacksTo(_playerSideEntityId, _shopEntityId);
        TransferAllStacksTo(_shopSideEntityId, world.PlayerEntityId);
        ShopActions.TryGiveCurrencyToShop(componentManager, _shopPool, eventBus, _playerSideEntityId, _shopEntityId, CurrencyType.Gold, eventPlayerEntityId: world.PlayerEntityId);
        CurrencyActions.TryTransfer(componentManager, _shopSideEntityId, world.PlayerEntityId, CurrencyType.Gold);
    }

    /// <summary>
    /// Rebuilds both columns' Gold from a clean slate every click, rather than only topping up the
    /// short side on top of whatever Gold already happens to be sitting there: first removes *all*
    /// Gold from both trade columns, returning each amount to its own real owner (the whole-balance
    /// `CurrencyActions.TryTransfer` overload, a no-op if a column already has none), so both
    /// columns' Values now reflect only their item contents. Then tops up whichever side that
    /// leaves short using *that side's own real, outside-the-trade currency*. If the payer can't
    /// fully cover the deficit, it adds as much as it has and stops -- values stay unequal, Complete
    /// stays disabled.
    ///
    /// Confirmed live as the correction over an earlier version that only netted out
    /// min(playerColumnGold, shopColumnGold) before topping up: that left whichever side had more
    /// Gold to begin with still holding the leftover difference, rather than starting the
    /// rebalance from a fully Gold-free baseline on both sides.
    /// </summary>
    private void BalanceOffer()
    {
        if (_currencyPool is null)
        {
            return;
        }

        CurrencyActions.TryTransfer(componentManager, _playerSideEntityId, world.PlayerEntityId, CurrencyType.Gold);
        CurrencyActions.TryTransfer(componentManager, _shopSideEntityId, _shopEntityId, CurrencyType.Gold);

        var playerValue = ComputeColumnValue(_playerSideEntityId, isShopSide: false);
        var shopValue = ComputeColumnValue(_shopSideEntityId, isShopSide: true);

        if (shopValue > playerValue)
        {
            var deficit = shopValue - playerValue;
            _currencyPool.TryGetReadonly(world.PlayerEntityId, out var realPlayerCurrency);
            var moveAmount = System.Math.Min(deficit, realPlayerCurrency.Gold);
            if (moveAmount > 0)
            {
                CurrencyActions.TryTransfer(componentManager, world.PlayerEntityId, _playerSideEntityId, CurrencyType.Gold, moveAmount);
            }
        }
        else if (playerValue > shopValue)
        {
            var deficit = playerValue - shopValue;
            _currencyPool.TryGetReadonly(_shopEntityId, out var realShopCurrency);
            var moveAmount = System.Math.Min(deficit, realShopCurrency.Gold);
            if (moveAmount > 0)
            {
                CurrencyActions.TryTransfer(componentManager, _shopEntityId, _shopSideEntityId, CurrencyType.Gold, moveAmount);
            }
        }
    }

    /// <summary>
    /// The two header labels ("Player Value"/"Shop Value" plus each one's own value line) are
    /// drawn directly here, not as child TextWindow elements -- TextWindow.DrawContent always
    /// flushes left against LinePadding, with no centered-text mode, and these need to read
    /// centered over their own column. LabelRenderer.DrawCentered (already used to center map
    /// glyphs) works for an arbitrary string just as well, and already gets the same whole-pixel
    /// rounding fix every other UI text draw does (see its own doc comment).
    /// </summary>
    public override void DrawContent(GameTime gameTime)
    {
        base.DrawContent(gameTime);

        var spriteBatch = ElementPoolService.SpriteBatch;
        var labelFont = FontService.GetFont(FontChrome.DefaultFontSize);

        DrawHeaderColumn(spriteBatch, labelFont, 0, "Player Value", _playerValueText);
        DrawHeaderColumn(spriteBatch, labelFont, ColumnSize.X + ColumnGap, "Shop Value", _shopValueText);
    }

    private void DrawHeaderColumn(SpriteBatch spriteBatch, SpriteFontBase font, float x, string label, string valueText)
    {
        var columnTopLeft = ContentAbsolutePosition + new Vector2(x, 0);
        var labelFootprint = new Vector2(ColumnSize.X, HeaderLabelLineHeight);
        var valueFootprint = new Vector2(ColumnSize.X, HeaderHeight - HeaderLabelLineHeight);

        LabelRenderer.DrawCentered(spriteBatch, font, label, columnTopLeft, labelFootprint, Color.White);
        LabelRenderer.DrawCentered(spriteBatch, font, valueText, columnTopLeft + new Vector2(0, HeaderLabelLineHeight), valueFootprint, Color.White);
    }

    private void BuildColumn(float x, int entityId, bool isShopSide)
    {
        var gridWindow = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(x, HeaderHeight), Size = GridSize, DisplayMode = ElementDisplayMode.Fixed },
            // No CanUserScrollVertical -- the 20-stack cap (InventoryCapacity.MaxNonPlayerStackCount)
            // and this grid's own 2x10 size are chosen to match exactly, so scrolling never applies.
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            // Transparent, not the usual PanelContentColor -- confirmed live look for the trade
            // grid specifically (CurrencyRowContent's own footer just below keeps PanelContentColor).
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });

        // See ShopWindow.BuildGrid's own doc comment -- same flush-content fix for the same
        // clipped-bottom-row bug.
        gridWindow.ContentPadding = Vector2.Zero;

        // getSecondaryTargetEntityId/onItemSelected/onCompareRequested are all no-ops for now --
        // trade-grid drag/context-menu behavior (Add to trade, direct sell/buy, right-click-removes)
        // isn't wired yet, see this class's own doc comment. tradeGridIsShopSide: isShopSide picks
        // TradeItemStackCell and the correct buy/sell pricing direction for this column -- see
        // InventoryGridContent's own doc comment on that parameter. Each column gets its own
        // dedicated hover popup, not a shared one -- see _playerColumnHoverPopup's own doc comment.
        var hoverPopup = isShopSide ? _shopColumnHoverPopup : _playerColumnHoverPopup;
        gridWindow.SetContent(new InventoryGridContent(world, componentManager, itemCatalog, ElementPoolService, FontService, LabelRenderer, spriteSheetService, spriteRenderer, contextMenuController, entityId, filterTag: null, hoverPopup, static () => null, mapViewState, static (_, _) => { }, static (_, _) => { }, isShopSide));
        AddChild(gridWindow);

        var footerWindow = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(x, HeaderHeight + GridSize.Y), Size = new Vector2(ColumnSize.X, CurrencyRowContent.Height), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
        });
        footerWindow.ContentPadding = Vector2.Zero;
        // showLabels: false -- "10 [sprite]", not "Gold : 10 [sprite]"; this column is too narrow
        // to spare the label (per the ask). textColor: white -- confirmed live look, matching the
        // trade grid's own transparent background just above.
        footerWindow.SetContent(new CurrencyRowContent(entityId, componentManager, world, contextMenuController, ElementPoolService, FontService, LabelRenderer, spriteSheetService, spriteRenderer, static () => null, eventBus: null, showLabels: false, textColor: Color.White));
        AddChild(footerWindow);
    }

    private void BuildFooterButtons()
    {
        var buttonsTop = HeaderHeight + GridSize.Y + CurrencyRowContent.Height + SectionGap;

        _balanceOfferButton = CreateButton("Balance Offer", new Vector2(0, buttonsTop), new Vector2(ContentWidth, ButtonHeight));

        var secondRowTop = buttonsTop + ButtonHeight + SectionGap;
        var halfWidth = (ContentWidth - ColumnGap) / 2f;
        _cancelButton = CreateButton("Cancel", new Vector2(0, secondRowTop), new Vector2(halfWidth, ButtonHeight));
        _completeButton = CreateButton("Complete", new Vector2(halfWidth + ColumnGap, secondRowTop), new Vector2(halfWidth, ButtonHeight));

        // Both default disabled on the empty trade this window always opens with -- Update's own
        // per-frame recompute takes over the moment anything's added to either column.
        _balanceOfferButton.Enabled = false;
        _completeButton.Enabled = false;

        _balanceOfferButton.Clicked += _ => BalanceOffer();

        // Routed through OnCancelClicked, not a direct Close() call -- TradeWindowController needs
        // to tell Cancel apart from every other way this window can close so it can decide whether
        // to run ReturnEverythingToOwners (see OnCancelClicked's own doc comment) -- all four close
        // paths cascade to the shop/inventory windows alike now, so this is no longer about that.
        _cancelButton.Clicked += _ => OnCancelClicked?.Invoke();

        // CompleteTrade runs the swap here (this class owns componentManager/the entity ids), then
        // OnCompleteClicked lets TradeWindowController do the same "mark the reason, then Close()"
        // sequencing Cancel already uses -- see OnCompleteClicked's own doc comment for why the
        // reason matters (ReturnEverythingToOwners must NOT also run for this close).
        _completeButton.Clicked += _ =>
        {
            CompleteTrade();
            OnCompleteClicked?.Invoke();
        };
    }

    private Button CreateButton(string text, Vector2 position, Vector2 size)
    {
        var button = ElementPoolService.CreateElement<Button>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.ControlBackground },
            Text = new TextOptions { Text = text, TextColor = WindowPalette.ControlLabelTextColor },
        });
        AddChild(button);
        return button;
    }
}
