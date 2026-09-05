using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules;
using Game.Modules.Actions.Activators;
using Game.Modules.Currency.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops;
using Game.Modules.Shops.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.Content;

/// <summary>
/// Grid of item-stack icons for one entity's inventory, optionally filtered to stacks whose item
/// carries filterTag (null shows every stack -- the "All" tab) -- one InventoryItemStackCell per
/// stack, no empty filler cells, wraps to hostWindow's width and scrolls vertically without limit
/// (the host tab body window already has CanUserScrollVertical -- see TabbedContent). Sorted
/// alphabetically by item name by default (SortOrder), further narrowed by NameFilter (a
/// case-insensitive Name.Contains match) and HideDisabled -- all three are settable properties
/// driven by GridControl via InventoryTabContent, this class has no idea either of those exist.
/// Rebuilds (destroy-all, recreate-all -- cheap, since this only fires on an actual inventory
/// mutation or a property change, not every frame) whenever the pool's per-entity version
/// changes, or SortOrder/NameFilter/HideDisabled actually changes. Also self-polls
/// Mouse.GetState() every Update (see UpdateHover), the same idiom AbilityScoreWindow uses for
/// its own hover popup -- kept self-contained here rather than routed through UiInputController
/// since nothing else needs to know about it. Shows/hides through the shared TooltipController
/// (see its own doc comment) rather than owning a private Tooltip -- this instance itself is what
/// TooltipController.Show/Hide's owner identity is, since a specific InventoryGridContent instance
/// is already exactly "one independent poller" (one per tab, or per Trade column).
/// </summary>
public sealed class InventoryGridContent(
    World world,
    ComponentManager componentManager,
    ItemCatalog itemCatalog,
    ElementPoolService elementPoolService,
    FontService fontService,
    LabelRenderer labelRenderer,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    ContextMenuController contextMenuController,
    int entityId,
    Tag? filterTag,
    TooltipController tooltipController,
    Func<int?> getSecondaryTargetEntityId,
    MapViewState mapViewState,
    Action<int, Guid> onItemSelected,
    Action<int, Guid> onCompareRequested,
    // Null (every non-trade caller) -- this grid's own pricing direction/cell type derive from
    // mapViewState.OpenShopEntityId/entityId as they always have. Non-null only for the trade
    // window's own two columns (PLAN-trade-window.md), which need TradeItemStackCell instead of
    // Shop/InventoryItemStackCell and a pricing direction that entityId == shopEntityId can never
    // express (neither trade-offer entity is ever the real shop) -- true for the shop-side column
    // (buy pricing, same direction the real shop grid uses), false for the player-side column
    // (sell pricing, same direction the real player grid uses). See IsThisGridTheShop's own use
    // below for every place this substitutes for the ordinary entity-id check.
    bool? tradeGridIsShopSide = null) : IElementContent, IInventoryDropTarget
{
    /// <summary>50% larger than the original (24,24) for readability. internal, not private -- SecondaryInventoryWindow/ShopWindow both derive their own fixed grid width from this and CellGap rather than hand-duplicating the numbers (see their own doc comments on why that duplication was a landmine).</summary>
    public static readonly Vector2 CellSize = new(36, 36);

    /// <summary>Width multiplier for a shop-mode cell (see ShopItemStackCell) -- 4x wider than CellSize at the same height, room for sprite + name + price instead of just an icon. internal, not private -- ShopWindow derives its own fixed grid width from this and CellSize/CellGap rather than hand-duplicating the number.</summary>
    internal const float ShopCellWidthMultiplier = 4f;

    internal const float CellGap = 1f;

    /// <summary>The entity whose inventory this grid displays -- what UiInputController's content-drag path reads to identify a drop target's owning entity (see InventoryActions.TryTransferStack).</summary>
    public int EntityId => entityId;

    /// <summary>Popup sits just to the right of whatever's hovered, vertically centered against it -- see PopupPositioning.GetPosition(East).</summary>
    private static readonly Vector2 PopupGap = new(1, 1);

    /// <summary>Same LightGreen/LightCoral pair ShopItemStackCell's own price-line color and ItemDetailsWindow's BetterColor/WorseColor already use (LightCoral, not IndianRed -- see ItemDetailsWindow.WorseColor's own doc comment for why) -- see ComputeHoverRows' own doc comment for the favorable/unfavorable-per-grid-direction rule these color.</summary>
    private static readonly Color FavorableStatusColor = Color.LightGreen;

    private static readonly Color UnfavorableStatusColor = Color.LightCoral;

    private readonly MultiComponentPool<InventoryItemStackComponent> _stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
    private readonly PackedComponentPool<ShopComponent>? _shopPool = componentManager.IsRegistered<ShopComponent>() ? componentManager.GetPackedPool<ShopComponent>() : null;
    private readonly PackedComponentPool<CurrencyComponent>? _currencyPool = componentManager.IsRegistered<CurrencyComponent>() ? componentManager.GetPackedPool<CurrencyComponent>() : null;
    private readonly List<InventoryItemStackComponent> _reusableStacks = [];
    private readonly List<(InventoryItemStackComponent Stack, ItemDefinition Definition)> _reusableVisibleEntries = [];
    private readonly Dictionary<Guid, List<int>> _reusableGroupIndices = [];
    private readonly List<CellEntry> _reusableCellEntries = [];
    private readonly List<InventoryItemStackCell> _cells = [];

    private readonly VersionWatcher _versionWatcher = new();

    private Window _hostWindow = null!;

    private InventoryItemStackCell? _hoveredCell;
    private int _hoveredFrames;
    private bool _closeHookupSubscribed;

    private InventorySortOrder _sortOrder = InventorySortOrder.NameAscending;
    private string _nameFilter = string.Empty;
    private bool _hideDisabled;
    private bool _groupDivergedStacks = true;

    /// <summary>Which single ItemDefinitionId (if any) is currently shown expanded -- ungrouped into its individual member cells -- instead of merged into one badged cell. Cleared by any filter/sort/grouping-toggle change and by Deactivate (tab switch); set/cleared by OnCellClicked.</summary>
    private Guid? _expandedItemDefinitionId;

    /// <summary>
    /// One rendered cell's resolved display data, computed once per RebuildCells after grouping --
    /// decoupled from "how many underlying stacks" since a merged cell has no single one. Quantity
    /// is what's actually shown (a group's summed total, or one stack's own); SortQuantity is what
    /// sort order compares against, and is deliberately the *group's* total for every member of a
    /// currently-expanded group (not each member's own Quantity) so they sort to the same key and
    /// land contiguously in the final layout regardless of active SortOrder -- see RebuildCells'
    /// own border-drawing pass, which assumes exactly that adjacency. No charge-count field --
    /// a wand's remaining/max charges used to replace this cell's own quantity badge outright, but
    /// that's shown in the hover tooltip now (see UpdateHover's own "Charges: {n}/{max}" line), so
    /// the badge just shows the plain Quantity, the same as any other item, charges or not.
    /// SortFirstAcquiredUtcTicks mirrors SortQuantity's own group-key behavior: one stack's own
    /// InventoryItemStackComponent.FirstAcquiredUtcTicks normally, but the *newest* value across
    /// every member for a currently-expanded group's own member cells (same contiguity reasoning
    /// as SortQuantity) and for a merged badge cell (the "newest FirstAcquired of the item stacks
    /// in the merged stack" rule InventorySortOrder.RecentlyAcquiredDescending sorts by).
    /// </summary>
    private readonly record struct CellEntry(ItemDefinition Definition, Guid? StackInstanceId, ushort Quantity, ushort SortQuantity, long SortFirstAcquiredUtcTicks, bool IsDisabled, bool IsDivergent, bool MergedStackBadgeVisible);

    /// <summary>Defaults to NameAscending, reproducing this class's original always-alphabetical behavior exactly. Setting to the same value is a no-op -- doesn't force a rebuild.</summary>
    public InventorySortOrder SortOrder
    {
        get => _sortOrder;
        set
        {
            if (_sortOrder == value)
            {
                return;
            }

            _sortOrder = value;
            _expandedItemDefinitionId = null;
            RebuildIfInitialized();
        }
    }

    /// <summary>Case-insensitive contains-match against each visible item's Name. Empty (the default) matches everything.</summary>
    public string NameFilter
    {
        get => _nameFilter;
        set
        {
            value ??= string.Empty;
            if (_nameFilter == value)
            {
                return;
            }

            _nameFilter = value;
            _expandedItemDefinitionId = null;
            RebuildIfInitialized();
        }
    }

    /// <summary>False (the default) shows disabled stacks the same as always -- grayed via InventoryItemStackCell's own icon tint (its background is transparent, so the tint is the only disabled cue). True hides them entirely instead.</summary>
    public bool HideDisabled
    {
        get => _hideDisabled;
        set
        {
            if (_hideDisabled == value)
            {
                return;
            }

            _hideDisabled = value;
            _expandedItemDefinitionId = null;
            RebuildIfInitialized();
        }
    }

    /// <summary>
    /// True (the default) merges every stack sharing an ItemDefinitionId into one cell with a
    /// summed quantity -- a "+" badge marks a merge containing at least one divergent stack (see
    /// InventoryItemStackCell's own doc comment); clicking a badged cell expands just that one item
    /// id into its individual stacks (see OnCellClicked). False disables merging entirely, for
    /// every item -- one cell per stack, the grid's original always-per-stack behavior.
    /// </summary>
    public bool GroupDivergedStacks
    {
        get => _groupDivergedStacks;
        set
        {
            if (_groupDivergedStacks == value)
            {
                return;
            }

            _groupDivergedStacks = value;
            _expandedItemDefinitionId = null;
            RebuildIfInitialized();
        }
    }

    /// <summary>How many cells the last rebuild actually produced, after tag/name/disabled filtering -- GridControl's item-count display reads this (see InventoryTabContent).</summary>
    public int VisibleItemCount { get; private set; }

    /// <summary>Property setters above can fire before Initialize (e.g. InventoryTabContent wiring GridControl's events right after CreateElement) -- RebuildCells needs _hostWindow, so skip until Initialize's own unconditional rebuild has run at least once.</summary>
    private void RebuildIfInitialized()
    {
        if (_hostWindow is not null)
        {
            RebuildCells();
        }
    }

    /// <summary>
    /// Rebuilds unconditionally, not gated behind the version watcher -- TabbedContent reuses the
    /// same InventoryGridContent instance across repeated Deactivate/Initialize cycles as the
    /// player switches tabs back and forth (see SwitchTab), and Deactivate always fully tears
    /// down _cells/_hostWindow's children. Gating this on _versionWatcher.HasChanged would skip
    /// the rebuild on every re-Initialize after the first, since the underlying stack version
    /// hasn't actually changed just from switching tabs -- confirmed bug: a tab's grid was empty
    /// on every selection after its first. Primes the watcher afterward so Update's own check
    /// doesn't immediately redo the same rebuild this frame.
    /// </summary>
    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        hostWindow.Resized += OnHostWindowResized;

        // Never assigned via hostWindow.SetContent (see this class's other consumer,
        // InventoryTabContent, whose own doc comment explains why: this instance's own Update is
        // driven manually, not through hostWindow.Content) -- Tag is what lets a caller (e.g.
        // UiInputController's content-drag drop resolution) still identify "this window hosts
        // entityId's inventory grid" regardless of which of the two hosting patterns built it.
        hostWindow.Tag = this;

        // hostWindow itself may only be an inner child (e.g. one of TradeWindow's two column grid
        // windows, or a tab body inside InventoryManagementWindow's TabbedContent) -- Window.Close()
        // only invokes Closed on the exact window it's called on, and a child torn down as part of
        // its parent's own recursive close (ElementPoolService.CloseAllChildren) never fires its
        // own Closed at all. Walking up to whichever ancestor genuinely has no Window parent finds
        // the one that's actually .Close()'d directly, so the tooltip still hides itself the moment
        // the real top-level window closes, regardless of how deep this grid is nested inside it.
        // Guarded to run once per instance -- TabbedContent re-Initializes the same instance every
        // time the player switches back to its tab (see this method's own doc comment), which would
        // otherwise pile up one duplicate subscription per switch.
        if (!_closeHookupSubscribed)
        {
            _closeHookupSubscribed = true;
            var root = hostWindow;
            while (root.ParentElement is Window parentWindow)
            {
                root = parentWindow;
            }

            root.Closed += _ => tooltipController.Hide(this);
        }

        _isShopMode = mapViewState.OpenShopEntityId is not null;
        RebuildCells();
        _versionWatcher.HasChanged(_stacks.GetEntityVersion(entityId));
    }

    /// <summary>Whichever cell type/size is currently active -- ShopItemStackCell at 4x width while a shop is open (see MapViewState.OpenShopEntityId), plain InventoryItemStackCell at CellSize otherwise. Read by both this grid's own layout (RebuildCells/ComputeColumnCount) and, transiently, by whatever triggered the mode change.</summary>
    private bool _isShopMode;

    /// <summary>TradeItemStackCell stays a plain CellSize square -- unlike ShopItemStackCell, it has no name to make room for (see TradeItemStackCell's own doc comment), so it doesn't need the extra width.</summary>
    private Vector2 ActiveCellSize => tradeGridIsShopSide is not null ? CellSize : _isShopMode ? new Vector2(CellSize.X * ShopCellWidthMultiplier, CellSize.Y) : CellSize;

    /// <summary>True when this grid represents the shop's own side of a buy/sell (BuyMultiplier pricing), false for the paying side (SellMultiplier pricing) -- ordinarily entityId == shopEntityId, but tradeGridIsShopSide overrides this for the trade window's own two columns, neither of which is ever the real shop entity. See tradeGridIsShopSide's own doc comment (constructor parameter list).</summary>
    private bool IsThisGridTheShop(int shopEntityId) => tradeGridIsShopSide ?? entityId == shopEntityId;

    /// <summary>
    /// shopEntityId's own current stock of itemDefinitionId, plus whatever's currently staged in
    /// the trade window's shop-side column for that same item (if a trade window even exists) --
    /// what a trade grid's own cells (this file, via EffectiveStockForThisGrid below) and
    /// TradeWindow's own Header: Player Value/Shop Value computation both price against, instead of
    /// a bare ShopStockPricing.GetTotalStock(shopEntityId, ...) call. A stack staged in the
    /// trade-shop column has already physically moved off the real shop entity (a plain
    /// InventoryActions.TryTransferStack, see UiInputController.ResolveTradeAwareItemDrag's "Shop
    /// grid &lt;-&gt; Trade: shop column" branch), but hasn't actually left the shop's *true*
    /// ownership yet -- no Gold has changed hands, the trade could still be cancelled. Without this,
    /// a stack that has entirely left the shop's own pool reads as 0 stock, which
    /// ShopStockPricing.ComputeBulkPrice/ComputeBulkBreakdown clamp down to a single stock level --
    /// pricing only 1 unit of a bulk stack regardless of its real Quantity (confirmed live as the
    /// trade window's own "buying 5 scrolls back out of the trade column only charges for 1" bug).
    /// A no-op (equal to the plain GetTotalStock call) whenever no trade window exists at all, or
    /// shopEntityId somehow already *is* the trade-shop entity (never true in practice, just
    /// defensive). Static and internal, not an instance method here -- TradeWindow's own Header
    /// computation needs the identical correction without owning an InventoryGridContent instance
    /// of its own. Deliberately NOT used for the real shop's own grid or the player's own grid while
    /// shop mode is active outside a trade column (see EffectiveStockForThisGrid) -- ShopActions.
    /// TryBuyFromShop/TrySellToShop, the methods that actually charge/cap a direct buy or sell, only
    /// ever read the shop's plain physical stock, so pricing/gating those two grids against the
    /// trade-inclusive total here would let the displayed price and eligibility disagree with what
    /// a direct Buy All/Sell All (or a plain drag, bypassing the trade window entirely) actually
    /// charges.
    /// </summary>
    internal static int GetEffectiveShopStock(ComponentManager componentManager, MapViewState mapViewState, int shopEntityId, Guid itemDefinitionId)
    {
        var stock = ShopStockPricing.GetTotalStock(componentManager, shopEntityId, itemDefinitionId);
        if (mapViewState.ReservedEntityIds?.TradeOfferShopEntityId is { } tradeShopEntityId && tradeShopEntityId != shopEntityId)
        {
            stock += ShopStockPricing.GetTotalStock(componentManager, tradeShopEntityId, itemDefinitionId);
        }

        return stock;
    }

    /// <summary>
    /// The stock level THIS grid's own pricing/eligibility/status reads should walk -- GetEffectiveShopStock's
    /// trade-inclusive total only while this grid genuinely is one of the trade window's own two
    /// columns (tradeGridIsShopSide is not null), the shop's plain physical stock otherwise (the
    /// real shop's own grid, or the player's own grid while shop mode is active but no trade column
    /// is involved). See GetEffectiveShopStock's own doc comment for why the two grids must not
    /// share the trade-inclusive number.
    /// </summary>
    private int EffectiveStockForThisGrid(int shopEntityId, Guid itemDefinitionId) =>
        tradeGridIsShopSide is not null
            ? GetEffectiveShopStock(componentManager, mapViewState, shopEntityId, itemDefinitionId)
            : ShopStockPricing.GetTotalStock(componentManager, shopEntityId, itemDefinitionId);

    public void Update(GameTime gameTime)
    {
        var shopModeNow = mapViewState.OpenShopEntityId is not null;
        if (shopModeNow != _isShopMode)
        {
            _isShopMode = shopModeNow;
            RebuildCells();
        }
        else if (_versionWatcher.HasChanged(_stacks.GetEntityVersion(entityId)))
        {
            RebuildCells();
        }

        UpdateHover(Mouse.GetState());
        UpdateSelection();
        UpdateCompareState();
    }

    /// <summary>Direct per-cell field set every frame, no rebuild -- mirrors UpdateHover's own IsHovered sync exactly, since selection (driven by the Item Details window, opened/changed/closed independently of this grid) can change without this grid ever rebuilding.</summary>
    private void UpdateSelection()
    {
        foreach (var cell in _cells)
        {
            cell.IsSelected = cell.StackInstanceId is not null && cell.StackInstanceId == mapViewState.SelectedItemStackInstanceId;
        }
    }

    /// <summary>
    /// Direct per-cell field set every frame, no rebuild -- mirrors UpdateSelection exactly. Shop
    /// mode takes priority over Item Details Comparison (the two are never both meaningfully
    /// active for the same grid in practice, and a shop's own buy/sell eligibility is the more
    /// urgent signal while one is open): while MapViewState.OpenShopEntityId is set, every cell's
    /// CompareState instead reflects shop trade eligibility (see UpdateShopEligibilityState).
    /// Otherwise None (not compare-armed) for every cell when MapViewState.CompareRequiredActivatorType
    /// is null; Ineligible for a Merged Stack cell (no single stack to add) or one whose effective
    /// item's Activator concrete type doesn't match, Eligible when it does.
    /// </summary>
    private void UpdateCompareState()
    {
        if (mapViewState.OpenShopEntityId is { } shopEntityId)
        {
            UpdateShopEligibilityState(shopEntityId);
            return;
        }

        foreach (var cell in _cells)
        {
            cell.CanStageInTrade = false; // Never meaningful outside shop mode -- see its own doc comment.

            if (mapViewState.CompareRequiredActivatorType is not { } requiredType)
            {
                cell.CompareState = CellCompareState.None;
                continue;
            }

            if (cell.StackInstanceId is not { } stackInstanceId ||
                !InventoryQueries.TryFindByStackInstanceId(_stacks, cell.EntityId, stackInstanceId, out var stack) ||
                !InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var definition))
            {
                cell.CompareState = CellCompareState.Ineligible;
                continue;
            }

            cell.CompareState = definition.Activator?.GetType() == requiredType ? CellCompareState.Eligible : CellCompareState.Ineligible;
        }
    }

    /// <summary>
    /// Eligible when this cell's item can be traded with shopEntityId's own ShopComponent (tag
    /// match) AND whichever side would be paying Gold can afford it -- the shop's own grid checks
    /// the player's Gold (a purchase), the player's own grid checks the shop's Gold (a sale). A
    /// Merged Stack cell (no single stack to price) is always Ineligible, same as compare mode's
    /// own handling. Ineligible cells grey out (existing isGreyedOut logic) and refuse to drag/
    /// Give/Take/Sell All/Buy All (see UiInputController.TryStartContentDrag and
    /// BuildItemContextMenu below) -- closes the currency-drain-style exploit a naive reuse of
    /// plain Give/Take would otherwise open for wrong-tag or unaffordable trades. CanStageInTrade
    /// is set independently (tag match only, ignoring affordability) -- see its own doc comment for
    /// why an unaffordable item can still be greyed out here yet still draggable into the trade
    /// window.
    /// </summary>
    private void UpdateShopEligibilityState(int shopEntityId)
    {
        if (_shopPool is null || _currencyPool is null || !_shopPool.TryGetReadonly(shopEntityId, out var shop))
        {
            foreach (var cell in _cells)
            {
                cell.CompareState = CellCompareState.Ineligible;
                cell.CanStageInTrade = false;
            }

            return;
        }

        var isThisGridTheShop = IsThisGridTheShop(shopEntityId);
        var payerEntityId = isThisGridTheShop ? world.PlayerEntityId : shopEntityId;
        _currencyPool.TryGetReadonly(payerEntityId, out var payerCurrency);

        foreach (var cell in _cells)
        {
            if (cell.StackInstanceId is not { } stackInstanceId ||
                !InventoryQueries.TryFindByStackInstanceId(_stacks, cell.EntityId, stackInstanceId, out var stack) ||
                !InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var definition) ||
                !ShopActions.CanTrade(shop, definition))
            {
                cell.CompareState = CellCompareState.Ineligible;
                cell.CanStageInTrade = false;
                continue;
            }

            cell.CanStageInTrade = true;

            var effectiveStock = EffectiveStockForThisGrid(shopEntityId, definition.Id);
            var preferredStockLevel = ShopStockPricing.GetPreferredStockLevel(componentManager, shopEntityId, definition.Id);
            var totalPrice = isThisGridTheShop
                ? ShopStockPricing.ComputeBulkBuyPrice(effectiveStock, preferredStockLevel, shop, definition, stack.Quantity)
                : ShopStockPricing.ComputeBulkSellPrice(effectiveStock, preferredStockLevel, shop, definition, stack.Quantity);
            cell.CompareState = payerCurrency.Gold >= totalPrice ? CellCompareState.Eligible : CellCompareState.Ineligible;
        }
    }

    /// <summary>
    /// Header highlight is immediate (instant visual feedback); the popup itself is delay-gated
    /// against the same shared HudChrome.HoverTooltipDelayFrames AbilityScoreWindow/
    /// HotbarController use -- but hides immediately on candidate change/loss (no delay on
    /// hiding, only on showing, same convention MapViewState.HoverSlot uses).
    /// </summary>
    private void UpdateHover(MouseState mouseState)
    {
        var mousePosition = new Point(mouseState.X, mouseState.Y);
        var candidate = FindHoverCandidate(mousePosition);

        foreach (var cell in _cells)
        {
            cell.IsHovered = ReferenceEquals(cell, candidate);
        }

        if (candidate == _hoveredCell)
        {
            _hoveredFrames++;
        }
        else
        {
            _hoveredCell = candidate;
            _hoveredFrames = candidate is null ? 0 : 1;
        }

        if (candidate is null || _hoveredFrames < HudChrome.HoverTooltipDelayFrames)
        {
            tooltipController.Hide(this);
            return;
        }

        ItemDefinition? definition = null;
        var isSingleStack = candidate.StackInstanceId is not null;
        ushort? stackQuantity = null;
        if (candidate.StackInstanceId is { } stackInstanceId)
        {
            if (InventoryQueries.TryFindByStackInstanceId(_stacks, entityId, stackInstanceId, out var stack))
            {
                InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out definition);
                stackQuantity = stack.Quantity;
            }
        }
        else
        {
            // A merged cell (see CellEntry's own doc comment) has no single stack to resolve --
            // fall back to the plain catalog definition, the same "default" shape every member
            // shares the ItemDefinitionId of.
            itemCatalog.TryGet(candidate.ItemDefinitionId, out definition);
        }

        if (definition is null)
        {
            return;
        }

        var summary = definition.Summary;
        if (definition.Activator is { } activator)
        {
            summary = $"{summary}\nTarget: {activator.Targeting.Shape}";

            // Charges only makes sense for one specific physical stack -- a merged cell could be
            // averaging over several different charge counts, so it's suppressed there entirely
            // rather than showing a misleading single number.
            if (isSingleStack && activator is WandActivator wandActivator)
            {
                summary += $"\nCharges: {wandActivator.Charges}/{wandActivator.MaxCharges}";
            }
        }

        var rows = ComputeHoverRows(definition, stackQuantity);

        // Shop mode's band table needs a guaranteed-wide-enough box for its range-annotated rows
        // (e.g. "Understocked (10-14)") -- a plain description tooltip still shrinks to content as
        // before.
        tooltipController.Show(this, candidate.Rectangle, PopupAnchor.East, PopupGap, PopupChrome.HoverPopupMaximumSize, summary, definition.Name, rows, useFixedWidth: rows is not null);
    }

    /// <summary>
    /// The stock-band table (one row per StockStatus, each showing its own stock range alongside
    /// its price) under the description while shop mode is open -- null outside it. Shown
    /// unconditionally, not just when the current band is Overstocked/Understocked -- the whole
    /// point of a band table (PLAN-stock-based-shop-pricing.md's Phase 5) is showing the player
    /// where "now" sits on the curve, which is exactly as informative for a Normal-band item as any
    /// other. Rows are all one neutral color -- the shop's *current* band is marked with an
    /// inner-fade glow instead (green for Desperate/Understocked, red for Overstocked/Flooded, white
    /// for Normal -- a fixed mapping, not the favorable/unfavorable-per-grid-direction rule
    /// ShopItemStackCell's own price-line color still uses) rather than by coloring that row's text.
    /// A divider separates the table from the description above it.
    ///
    /// On the player's own grid specifically (never the shop's own -- buying from the shop has no
    /// analogous cap), a wrong-tag item or one the shop is already at ItemDefinition.
    /// MaximumShopStock for (the same hard sell-cap TrySellToShop itself enforces) instead shows a
    /// single "Shop will not buy" row -- the band table would misleadingly imply a sale is still
    /// possible, just pricier.
    ///
    /// If stackQuantity has a value (any real quantity to price, including 1), a per-trade bracket
    /// receipt (PLAN-stock-based-shop-pricing.md's Phase 5) is always appended below the band table,
    /// past a second divider -- one row per band the trade actually crosses plus a Total row when it
    /// crosses more than one band, or just the Total alone when it stays within a single band (that
    /// band's own row in the table above already shows the identical per-unit price, so listing it
    /// again as a one-line "receipt" would be redundant). Omitted entirely only when there's no
    /// concrete quantity to price at all (a merged cell with no single stack behind it).
    /// </summary>
    private IReadOnlyList<TooltipRow>? ComputeHoverRows(ItemDefinition definition, ushort? stackQuantity)
    {
        if (!_isShopMode || mapViewState.OpenShopEntityId is not { } shopEntityId || _shopPool is null || !_shopPool.TryGetReadonly(shopEntityId, out var shop))
        {
            return null;
        }

        var isThisGridTheShop = IsThisGridTheShop(shopEntityId);
        var neutralColor = WindowPalette.TitleTextColor; // The shared Tooltip's own default TextColor (see Tooltip.Build) -- no caller overrides it.

        var effectiveStock = EffectiveStockForThisGrid(shopEntityId, definition.Id);

        if (!isThisGridTheShop)
        {
            var maximumShopStock = definition.MaximumShopStock ?? ShopStockPricing.DefaultMaximumShopStock;
            if (!ShopActions.CanTrade(shop, definition) || effectiveStock >= maximumShopStock)
            {
                return [TooltipRow.Divider(neutralColor), new TooltipRow("Shop will not buy", string.Empty, UnfavorableStatusColor)];
            }
        }

        var preferredStockLevel = ShopStockPricing.GetPreferredStockLevel(componentManager, shopEntityId, definition.Id);
        var maxStock = definition.MaximumShopStock ?? ShopStockPricing.DefaultMaximumShopStock;
        var (e1, e2, e3, e4) = ShopStockPricing.GetBandEdges(preferredStockLevel, maxStock);
        var currentBand = ShopStockPricing.GetStockStatus(effectiveStock, e1, e2, e3, e4);
        var shopMultiplier = isThisGridTheShop ? shop.BuyMultiplier : shop.SellMultiplier;

        var rows = new List<TooltipRow> { TooltipRow.Divider(neutralColor) };
        foreach (var band in ShopStockPricing.GetAllBands())
        {
            var (low, high) = ShopStockPricing.GetBandRange(band, e1, e2, e3, e4);
            var rangeText = band == StockStatus.Flooded ? $"{low}+" : $"{low}-{high}";
            var perUnitPrice = ShopStockPricing.GetBandPricePerUnit(definition, shopMultiplier, band);
            var glowColor = band == currentBand ? GlowColorFor(band) : (Color?)null;
            rows.Add(new TooltipRow(band.ToString(), $"{perUnitPrice}G", neutralColor, GlowColor: glowColor, MiddleText: rangeText));
        }

        if (stackQuantity is { } quantity)
        {
            var breakdown = isThisGridTheShop
                ? ShopStockPricing.ComputeBulkBuyBreakdown(effectiveStock, preferredStockLevel, shop, definition, quantity)
                : ShopStockPricing.ComputeBulkSellBreakdown(effectiveStock, preferredStockLevel, shop, definition, quantity);

            rows.Add(TooltipRow.Divider(neutralColor));

            var showPerBandRows = breakdown.Count > 1;
            var total = 0;
            foreach (var band in breakdown)
            {
                total += band.Subtotal;
                if (showPerBandRows)
                {
                    rows.Add(new TooltipRow($"{band.Units}x{band.PerUnitPrice}G", $"{band.Subtotal}G", neutralColor));
                }
            }

            rows.Add(new TooltipRow("Total", $"{total}G", neutralColor));
        }

        return rows;
    }

    /// <summary>Fixed color for whichever band is the shop's own *current* one on the band table -- green for Desperate/Understocked, red for Overstocked/Flooded, white for Normal. Unlike ShopItemStackCell.PriceIsFavorable/PriceIsUnfavorable, this does not flip with which grid is being drawn -- the band table always describes the shop's own stock, not a buy/sell direction.</summary>
    private static Color GlowColorFor(StockStatus band) => band switch
    {
        StockStatus.Desperate or StockStatus.Understocked => FavorableStatusColor,
        StockStatus.Overstocked or StockStatus.Flooded => UnfavorableStatusColor,
        _ => Color.White,
    };

    private InventoryItemStackCell? FindHoverCandidate(Point mousePosition)
    {
        foreach (var cell in _cells)
        {
            if (cell.Rectangle.Contains(mousePosition))
            {
                return cell;
            }
        }

        return null;
    }

    public void DrawContent(GameTime gameTime)
    {
        // Nothing to draw directly -- every stack is its own child InventoryItemStackCell,
        // which Window already draws as part of its own child-element loop.
    }

    /// <summary>Removes every cell -- called by TabbedContent when this tab is switched away from. Also hides the hover popup -- a no-op today (there's only one tab), but keeps this correct if a second tab is ever added.</summary>
    public void Deactivate()
    {
        elementPoolService.CloseAllChildren(_hostWindow);
        _cells.Clear();
        _hoveredCell = null;
        _hoveredFrames = 0;
        _expandedItemDefinitionId = null;
        tooltipController.Hide(this);
    }

    private void OnHostWindowResized(Element _) => RebuildCells();

    /// <summary>
    /// Clicking a badged (merged) cell expands its item id. A real single-stack cell click
    /// (standalone, or one of an already-expanded group's own member cells) opens/updates the
    /// Item Details window for that stack instead of collapsing anything -- the group needs to
    /// stay expanded while the player browses its members' details one at a time. Collapsing an
    /// expanded group back down therefore no longer happens via re-clicking one of its own
    /// members (there is no persistent badge cell to re-click while expanded -- see
    /// InventoryItemStackCell's own doc comment on Expansion Stacks replacing the badge entirely)
    /// -- instead, any click that lands on a cell *outside* the expanded group (a different
    /// item's own badge, an ungrouped item, or empty grid space handled by returning early above
    /// this method) still collapses it, the closest equivalent to the old "click anything else"
    /// rule now that a member click has its own dedicated meaning.
    /// </summary>
    private void OnCellClicked(Element element)
    {
        if (element is not InventoryItemStackCell cell)
        {
            return;
        }

        if (cell.MergedStackBadgeVisible)
        {
            _expandedItemDefinitionId = cell.ItemDefinitionId;
            RebuildCells();
            return;
        }

        if (cell.StackInstanceId is { } stackInstanceId)
        {
            onItemSelected(cell.EntityId, stackInstanceId);
        }

        var isExpandedMember = _expandedItemDefinitionId is not null && cell.ItemDefinitionId == _expandedItemDefinitionId;
        if (!isExpandedMember && _expandedItemDefinitionId is not null)
        {
            _expandedItemDefinitionId = null;
            RebuildCells();
        }
    }

    /// <summary>
    /// "Compare" (arms Item Details Comparison against this stack -- see ItemComparisonController.
    /// Arm), "Add to trade" (this grid's own entity -> the matching trade-offer entity, only while a
    /// shop -- and so a trade window -- is open, see below), plus "Give"/"Sell All" (this grid's own
    /// entity -> the currently-open secondary target) or "Take"/"Buy All" (the currently-open
    /// secondary target -> this grid's own entity) -- never both of the latter two, and neither if
    /// no secondary window is currently open (getSecondaryTargetEntityId returns null). Every option
    /// here is guarded on the clicked cell not being a Merged Stack (no single StackInstanceId to
    /// compare/transfer, the same restriction InventoryItemStackCell.CanBindToHotbar already
    /// enforces for drag-binding). For SecondaryInventoryWindow's own grid, getSecondaryTargetEntityId
    /// always returns that corpse's own entityId (it *is* the secondary target for as long as it
    /// exists), so its cells only ever offer "Take"; for the player's own grid, it queries whatever's
    /// actually open right now (see InventoryWindowController.GetSecondaryTargetEntityId), so "Give"
    /// only appears while a secondary window is open. Trade-grid cells never reach this method at all
    /// (see this grid's own OnRightClicked wiring in RebuildCells) -- PLAN-trade-window.md's own
    /// "Context menu changes" section makes right-click there remove the stack immediately instead.
    /// </summary>
    private List<ContextMenuOption> BuildItemContextMenu(InventoryItemStackCell cell)
    {
        List<ContextMenuOption> options = [];

        if (cell.StackInstanceId is not { } stackInstanceId)
        {
            return options;
        }

        options.Add(new ContextMenuOption("Compare", null, Enabled: true, () => onCompareRequested(cell.EntityId, stackInstanceId)));

        // While a shop is open, every cell's CompareState instead reflects shop trade eligibility
        // (see UpdateShopEligibilityState) -- Ineligible (wrong tag, or the paying side can't
        // afford it) means Give/Take must not even be offered, closing the same currency-drain-
        // style exploit a naive reuse of plain Give/Take would otherwise open.
        var isShopIneligible = mapViewState.OpenShopEntityId is not null && cell.CompareState == CellCompareState.Ineligible;

        // "Add to trade" -- gated on CanStageInTrade (tag match only, see its own doc comment), not
        // the stricter isShopIneligible (tag match AND affordability) Sell All/Buy All below use --
        // an unaffordable item can still be staged in the trade window even though it can't be
        // directly bought/sold there or on the shop's own grid; it just stays greyed out. Only on
        // the player's own grid (-> ReservedEntityIds.TradeOfferPlayerEntityId) or the real shop's
        // own grid (-> ReservedEntityIds.TradeOfferShopEntityId), never a corpse/container grid; a shop being open implies the
        // trade window is too (TradeWindowController opens in lockstep with ShopWindowController,
        // see its own doc comment), so OpenShopEntityId alone is enough to gate this without
        // separately checking the trade window exists.
        if (cell.CanStageInTrade && mapViewState.OpenShopEntityId is { } shopEntityId)
        {
            var tradeTargetEntityId = cell.EntityId == world.PlayerEntityId
                ? mapViewState.ReservedEntityIds?.TradeOfferPlayerEntityId
                : cell.EntityId == shopEntityId
                    ? mapViewState.ReservedEntityIds?.TradeOfferShopEntityId
                    : null;

            if (tradeTargetEntityId is { } tradeTarget)
            {
                options.Add(new ContextMenuOption("Add to trade", null, Enabled: true, () =>
                    InventoryActions.TryTransferStack(componentManager, cell.EntityId, tradeTarget, stackInstanceId, world)));
            }
        }

        if (!isShopIneligible && getSecondaryTargetEntityId() is { } secondaryTargetEntityId)
        {
            // A shop endpoint routes the same Give/Take gesture through ShopActions instead of a
            // plain transfer -- "Give" to a shop is a sale (ShopActions.TrySellToShop, relabeled
            // "Sell All"), "Take" from a shop is a purchase (ShopActions.TryBuyFromShop, relabeled
            // "Buy All"), both moving Gold the opposite direction at the shop's own price. A
            // non-shop secondary target (a corpse/container) keeps the plain "Give"/"Take" labels --
            // there's no price to speak of, just a transfer.
            var isShopSecondary = _shopPool?.Has(secondaryTargetEntityId) == true;

            if (cell.EntityId == world.PlayerEntityId && secondaryTargetEntityId != world.PlayerEntityId)
            {
                options.Add(new ContextMenuOption(isShopSecondary ? "Sell All" : "Give", null, Enabled: true, () =>
                {
                    if (isShopSecondary)
                    {
                        ShopActions.TrySellToShop(componentManager, itemCatalog, world.PlayerEntityId, secondaryTargetEntityId, stackInstanceId, world);
                    }
                    else
                    {
                        InventoryActions.TryTransferStack(componentManager, cell.EntityId, secondaryTargetEntityId, stackInstanceId, world);
                    }
                }));
            }
            else if (cell.EntityId == secondaryTargetEntityId && secondaryTargetEntityId != world.PlayerEntityId)
            {
                options.Add(new ContextMenuOption(isShopSecondary ? "Buy All" : "Take", null, Enabled: true, () =>
                {
                    if (isShopSecondary)
                    {
                        ShopActions.TryBuyFromShop(componentManager, itemCatalog, world.PlayerEntityId, secondaryTargetEntityId, stackInstanceId, world);
                    }
                    else
                    {
                        InventoryActions.TryTransferStack(componentManager, cell.EntityId, world.PlayerEntityId, stackInstanceId, world);
                    }
                }));
            }
        }

        return options;
    }

    /// <summary>
    /// Trade-grid cells' own right-click action, wired in place of BuildItemContextMenu/a context
    /// menu entirely (see that method's own doc comment) -- removes the clicked stack from the
    /// trade immediately, moving it back to whichever real entity it came from (the real player for
    /// the player-side column, the real shop for the shop-side column), the same transfer "Add to
    /// trade" performs in reverse. A no-op for a Merged Stack cell (no single StackInstanceId) or if
    /// isTradeShopSide's own real owner (OpenShopEntityId) is somehow unset.
    /// </summary>
    private void RemoveFromTrade(InventoryItemStackCell cell, bool isTradeShopSide)
    {
        if (cell.StackInstanceId is not { } stackInstanceId)
        {
            return;
        }

        var realOwnerEntityId = isTradeShopSide ? mapViewState.OpenShopEntityId : world.PlayerEntityId;
        if (realOwnerEntityId is { } destination)
        {
            InventoryActions.TryTransferStack(componentManager, cell.EntityId, destination, stackInstanceId, world);
        }
    }

    private void RebuildCells()
    {
        elementPoolService.CloseAllChildren(_hostWindow);
        _cells.Clear();

        InventoryQueries.CopyStacksForEntity(_stacks, entityId, _reusableStacks);

        _reusableVisibleEntries.Clear();
        foreach (var stack in _reusableStacks)
        {
            if (!InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var definition))
            {
                continue;
            }

            if (filterTag is { } tag && !definition.Tags.Contains(tag))
            {
                continue;
            }

            if (_hideDisabled && stack.IsDisabled)
            {
                continue;
            }

            if (_nameFilter.Length > 0 && !definition.Name.Contains(_nameFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _reusableVisibleEntries.Add((stack, definition));
        }

        BuildCellEntries();
        SortCellEntries();
        VisibleItemCount = _reusableCellEntries.Count;

        var columns = ComputeColumnCount();
        var cellSize = ActiveCellSize;

        for (var i = 0; i < _reusableCellEntries.Count; i++)
        {
            var entry = _reusableCellEntries[i];

            var column = i % columns;
            var row = i / columns;
            var position = new Vector2(column * (cellSize.X + CellGap), row * (cellSize.Y + CellGap));
            var isExpandedMember = _expandedItemDefinitionId is not null && entry.Definition.Id == _expandedItemDefinitionId && !entry.MergedStackBadgeVisible;

            var options = new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                // An expanded group's member cells skip their own normal border entirely -- the
                // group border (below) replaces it, drawn only on the group's outer perimeter, so
                // two adjacent members share a clean, unbroken join with no line between them.
                Layout = new ElementLayoutOptions { RelativePosition = position, Size = cellSize, DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = !isExpandedMember, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = Color.Transparent },
            };

            InventoryItemStackCell cell = tradeGridIsShopSide is not null
                ? elementPoolService.CreateElement<TradeItemStackCell>(_hostWindow, options)
                : _isShopMode
                    ? elementPoolService.CreateElement<ShopItemStackCell>(_hostWindow, options)
                    : elementPoolService.CreateElement<InventoryItemStackCell>(_hostWindow, options);

            cell.Configure(entityId, entry.Definition.Id, entry.StackInstanceId, entry.Definition.SpriteName, entry.Definition.Glyph, entry.Definition.GlyphColor, entry.Quantity, entry.IsDisabled, entry.IsDivergent, entry.MergedStackBadgeVisible, cellSize);

            if (cell is ShopItemStackCell shopCell)
            {
                shopCell.SetItemName(entry.Definition.Name);
                shopCell.SetPrice(ComputeShopTotalPrice(entry.Definition, entry.Quantity), entry.Quantity);
                shopCell.SetStockStatus(ComputeShopStockStatus(entry.Definition), tradeGridIsShopSide ?? mapViewState.OpenShopEntityId == entityId);
            }

            cell.Clicked += OnCellClicked;
            cell.OnRightClicked = tradeGridIsShopSide is { } isTradeShopSide
                ? _ => RemoveFromTrade(cell, isTradeShopSide)
                : position =>
                {
                    var options = BuildItemContextMenu(cell);
                    if (options.Count > 0)
                    {
                        contextMenuController.Open(new Vector2(position.X, position.Y), options);
                    }
                };
            _hostWindow.AddChild(cell);
            _cells.Add(cell);

            if (isExpandedMember)
            {
                // column == 0/columns - 1 guard the row-wrap case explicitly -- i-1/i+1 are still
                // valid indices across a row boundary (the previous row's last cell, or the next
                // row's first), but never a real visual left/right neighbor of this cell. i-columns/
                // i+columns need no equivalent guard: IsExpandedMemberAt's own bounds check already
                // returns false once that index runs off the top/bottom of the whole list.
                var top = !IsExpandedMemberAt(i - columns);
                var bottom = !IsExpandedMemberAt(i + columns);
                var left = column == 0 || !IsExpandedMemberAt(i - 1);
                var right = column == columns - 1 || !IsExpandedMemberAt(i + 1);
                cell.SetGroupBorderEdges(top, bottom, left, right);
            }
        }

        // Trade grids are fixed-size (InventoryCapacity.MaxNonPlayerStackCount, never scrolling --
        // see PLAN-trade-window.md's own "Trade grid capacity" section) and otherwise the only
        // grids in the game with visibly empty space below/beside their real items -- every other
        // grid either scrolls (no unused space to speak of) or is the shop's own grid (whose stock
        // is what it is). Filling the remainder with a pure decoration (never a real, interactable
        // cell -- see EmptyTradeSlotCell's own doc comment) makes it visually obvious the player can
        // still drop more in, not just that the grid happens to be smaller than 20 slots today.
        // Continues the same column/row math the real-cell loop above just used, so a placeholder
        // always lands in the next free slot after the last real item, never gets tracked in
        // _cells (never hoverable/selectable/compare-stated), and is destroyed and freshly
        // recreated every rebuild exactly like every real cell already is (see
        // elementPoolService.CloseAllChildren at the top of this method).
        if (tradeGridIsShopSide is not null)
        {
            for (var i = _reusableCellEntries.Count; i < InventoryCapacity.MaxNonPlayerStackCount; i++)
            {
                var column = i % columns;
                var row = i / columns;
                var position = new Vector2(column * (cellSize.X + CellGap), row * (cellSize.Y + CellGap));

                var emptyCell = elementPoolService.CreateElement<EmptyTradeSlotCell>(_hostWindow, new ElementOptions
                {
                    Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                    Layout = new ElementLayoutOptions { RelativePosition = position, Size = cellSize, DisplayMode = ElementDisplayMode.Fixed },
                    Chrome = new ElementChromeOptions { ShowBorder = false, CanUserFocus = false },
                    Content = new ElementContentOptions { ContentColor = Color.Transparent },
                });
                _hostWindow.AddChild(emptyCell);
            }
        }

        // A freshly created/pooled-and-reused cell's own CompareState defaults to (or still
        // carries, from its prior use in the pool) a stale value -- Configure never touches it,
        // only this per-frame sync does. Every RebuildCells call site used to rely on Update's own
        // later UpdateCompareState call to fix that up the same frame, but Initialize calls
        // RebuildCells directly with no such follow-up, so a grid's very first render (e.g. right
        // when a shop window opens, or right after a purchase lands a new stack in the player's
        // own inventory and triggers a rebuild) could draw one frame -- or, worse, persist until
        // the next unrelated Update -- with every cell showing whatever eligibility state its
        // pooled instance happened to carry over. Calling it here, unconditionally, at the end of
        // every rebuild closes that gap regardless of which caller triggered it (confirmed live:
        // a just-purchased item read as disabled in the player's own grid until manually
        // triggering an unrelated rebuild).
        UpdateCompareState();
    }

    private bool IsExpandedMemberAt(int index) =>
        index >= 0 && index < _reusableCellEntries.Count &&
        _reusableCellEntries[index].Definition.Id == _expandedItemDefinitionId &&
        !_reusableCellEntries[index].MergedStackBadgeVisible;

    private int ComputeColumnCount()
    {
        var cellSize = ActiveCellSize;
        return System.Math.Max(1, (int)((_hostWindow.ContentSize.X + CellGap) / (cellSize.X + CellGap)));
    }

    /// <summary>
    /// 0 outside shop mode or when the open shop's own ShopComponent can't be resolved -- callers
    /// only ever read this while building a ShopItemStackCell, which only happens while _isShopMode
    /// is true. isThisGridTheShop mirrors UpdateShopEligibilityState's own direction rule: this grid
    /// showing the shop's own stock prices a purchase, the player's own grid prices a sale. The real
    /// bulk-priced total for the whole stack -- what actually gets charged -- not a per-unit price;
    /// the cell itself no longer shows a per-unit breakdown (see ShopItemStackCell.SetPrice), the
    /// hover tooltip's own band table/receipt covers that in full.
    /// </summary>
    private int ComputeShopTotalPrice(ItemDefinition definition, int quantity)
    {
        if (mapViewState.OpenShopEntityId is not { } shopEntityId || _shopPool is null || !_shopPool.TryGetReadonly(shopEntityId, out var shop))
        {
            return 0;
        }

        var isThisGridTheShop = IsThisGridTheShop(shopEntityId);
        var effectiveStock = EffectiveStockForThisGrid(shopEntityId, definition.Id);
        var preferredStockLevel = ShopStockPricing.GetPreferredStockLevel(componentManager, shopEntityId, definition.Id);
        return isThisGridTheShop
            ? ShopStockPricing.ComputeBulkBuyPrice(effectiveStock, preferredStockLevel, shop, definition, (ushort)quantity)
            : ShopStockPricing.ComputeBulkSellPrice(effectiveStock, preferredStockLevel, shop, definition, (ushort)quantity);
    }

    /// <summary>
    /// The shop's own stock status for definition -- always keyed off the shop entity regardless of
    /// which grid is being built (the shop's own grid or the player's own grid while shop mode is
    /// active both describe the same shop's stock, see ComputeShopTotalPrice's own doc comment for
    /// the matching isThisGridTheShop direction rule). Normal (no color) outside shop mode or when
    /// the open shop's own ShopComponent can't be resolved -- same fallback ComputeShopTotalPrice
    /// uses.
    /// </summary>
    private StockStatus ComputeShopStockStatus(ItemDefinition definition)
    {
        if (mapViewState.OpenShopEntityId is not { } shopEntityId)
        {
            return StockStatus.Normal;
        }

        // The explicit-stock GetStockStatus overload, not the plain GetTotalStock-driven lookup --
        // this grid's own EffectiveStockForThisGrid may be the trade-inclusive total (see its own
        // doc comment), which a bare shopEntityId-only lookup would undercount.
        var maximumShopStock = definition.MaximumShopStock ?? ShopStockPricing.DefaultMaximumShopStock;
        var effectiveStock = EffectiveStockForThisGrid(shopEntityId, definition.Id);
        var preferredStockLevel = ShopStockPricing.GetPreferredStockLevel(componentManager, shopEntityId, definition.Id);
        return ShopStockPricing.GetStockStatus(effectiveStock, preferredStockLevel, maximumShopStock);
    }

    /// <summary>
    /// Groups _reusableVisibleEntries by ItemDefinitionId when GroupDivergedStacks is on --
    /// a group of exactly one stack (the overwhelmingly common case: every non-divergent item
    /// still only ever has one stack per id, per InventoryActions.AddItem's own merge behavior)
    /// renders unchanged; a larger group either merges into one badged cell (summed Quantity,
    /// first member's display data) or, if it's the currently-expanded id, renders as its
    /// individual member cells instead -- see CellEntry's own doc comment for SortQuantity's role
    /// in keeping an expanded group's members contiguous afterward.
    /// </summary>
    private void BuildCellEntries()
    {
        _reusableCellEntries.Clear();

        // Shop mode always shows one cell per physical stack, regardless of GroupDivergedStacks --
        // a Merged Stack cell has no single StackInstanceId (see InventoryItemStackCell's own doc
        // comment), so it can never actually be priced/given/taken/dragged to or from a shop
        // (BuildItemContextMenu and UiInputController's drag path both require one). The player's
        // own starting kit and a shop's own random stock draw from the same item catalog, so
        // buying something the player already carries some of (the common case, not an edge one)
        // would otherwise collapse into an untradeable Merged Stack the instant it landed --
        // confirmed live: a freshly bought item read as permanently disabled for selling back.
        if (!_groupDivergedStacks || _isShopMode)
        {
            foreach (var (stack, definition) in _reusableVisibleEntries)
            {
                _reusableCellEntries.Add(new CellEntry(definition, stack.StackInstanceId, stack.Quantity, stack.Quantity, stack.FirstAcquiredUtcTicks, stack.IsDisabled, stack.IsDivergent, MergedStackBadgeVisible: false));
            }

            return;
        }

        _reusableGroupIndices.Clear();
        for (var i = 0; i < _reusableVisibleEntries.Count; i++)
        {
            var itemId = _reusableVisibleEntries[i].Definition.Id;
            if (!_reusableGroupIndices.TryGetValue(itemId, out var indices))
            {
                indices = [];
                _reusableGroupIndices[itemId] = indices;
            }

            indices.Add(i);
        }

        foreach (var (itemId, indices) in _reusableGroupIndices)
        {
            if (indices.Count == 1)
            {
                var (stack, definition) = _reusableVisibleEntries[indices[0]];
                _reusableCellEntries.Add(new CellEntry(definition, stack.StackInstanceId, stack.Quantity, stack.Quantity, stack.FirstAcquiredUtcTicks, stack.IsDisabled, stack.IsDivergent, MergedStackBadgeVisible: false));
                continue;
            }

            var totalQuantity = 0;
            var anyDivergent = false;
            var allDisabled = true;
            var newestFirstAcquiredUtcTicks = long.MinValue;
            foreach (var index in indices)
            {
                var memberStack = _reusableVisibleEntries[index].Stack;
                totalQuantity += memberStack.Quantity;
                anyDivergent |= memberStack.IsDivergent;
                allDisabled &= memberStack.IsDisabled;
                newestFirstAcquiredUtcTicks = System.Math.Max(newestFirstAcquiredUtcTicks, memberStack.FirstAcquiredUtcTicks);
            }

            var groupTotal = (ushort)totalQuantity;

            if (itemId == _expandedItemDefinitionId)
            {
                foreach (var index in indices)
                {
                    var (stack, definition) = _reusableVisibleEntries[index];
                    _reusableCellEntries.Add(new CellEntry(definition, stack.StackInstanceId, stack.Quantity, groupTotal, newestFirstAcquiredUtcTicks, stack.IsDisabled, stack.IsDivergent, MergedStackBadgeVisible: false));
                }

                continue;
            }

            // A Merged Stack cell always shows its summed Quantity -- its members could each carry
            // a different charge count (now shown per-stack in the hover tooltip, not this badge),
            // but the quantity itself is exactly the sum regardless.
            var first = _reusableVisibleEntries[indices[0]];
            _reusableCellEntries.Add(new CellEntry(first.Definition, StackInstanceId: null, groupTotal, groupTotal, newestFirstAcquiredUtcTicks, allDisabled, IsDivergent: false, MergedStackBadgeVisible: anyDivergent));
        }
    }

    /// <summary>Sorts by SortQuantity, not each entry's own Quantity, so an expanded group's members (all sharing the same SortQuantity -- the group's total) land contiguously regardless of active SortOrder; ties (including two entries with genuinely equal Quantity/Name) always break by ItemDefinitionId, both for determinism and so a coincidental tie with an unrelated item can never split an expanded group apart.</summary>
    private void SortCellEntries()
    {
        switch (_sortOrder)
        {
            case InventorySortOrder.NameDescending:
                _reusableCellEntries.Sort(static (a, b) => CompareWithTieBreak(string.CompareOrdinal(b.Definition.Name, a.Definition.Name), a, b));
                break;
            case InventorySortOrder.QuantityDescending:
                _reusableCellEntries.Sort(static (a, b) => CompareWithTieBreak(b.SortQuantity.CompareTo(a.SortQuantity), a, b));
                break;
            case InventorySortOrder.QuantityAscending:
                _reusableCellEntries.Sort(static (a, b) => CompareWithTieBreak(a.SortQuantity.CompareTo(b.SortQuantity), a, b));
                break;
            case InventorySortOrder.RecentlyAcquiredDescending:
                _reusableCellEntries.Sort(static (a, b) => CompareWithTieBreak(b.SortFirstAcquiredUtcTicks.CompareTo(a.SortFirstAcquiredUtcTicks), a, b));
                break;
            default:
                _reusableCellEntries.Sort(static (a, b) => CompareWithTieBreak(string.CompareOrdinal(a.Definition.Name, b.Definition.Name), a, b));
                break;
        }
    }

    private static int CompareWithTieBreak(int primaryComparison, CellEntry a, CellEntry b) =>
        primaryComparison != 0 ? primaryComparison : a.Definition.Id.CompareTo(b.Definition.Id);
}
