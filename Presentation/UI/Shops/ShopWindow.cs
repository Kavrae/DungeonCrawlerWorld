using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Content;
using Presentation.UI.Looting;

namespace Presentation.UI.Shops;

/// <summary>
/// The shop side of trading: a fixed summary (icon, name, description) above a plain item grid --
/// copies SecondaryInventoryWindow's own layout shape (see its own doc comment for why every
/// position/size below is explicit rather than ambient-propagated) rather than extending it, since
/// a shop's summary carries no killer/died-tick (it's never a corpse) and its grid must eventually
/// grow shop-specific price display (see MapViewState.OpenShopEntityId's own doc comment) --
/// SecondaryInventoryWindow's own summary/grid are corpse-specific enough that bending them to fit
/// would cost more than a focused sibling. Phase 4 of the Shops plan is what actually switches this
/// window's grid (and the player's own) to the wider, price-showing cell layout; for now it's the
/// same InventoryGridContent every other inventory grid uses, with buy/sell already price-aware via
/// ShopActions (see InventoryGridContent.BuildItemContextMenu and UiInputController.ResolveContentDrag).
/// </summary>
public sealed class ShopWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    LabelRenderer labelRenderer,
    ComponentManager componentManager,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    ItemCatalog itemCatalog,
    World world,
    ContextMenuController contextMenuController,
    MapViewState mapViewState)
    : Window(fontService, elementPoolService, labelRenderer)
{
    private static readonly Vector2 IconSize = new(48, 48);
    private const float Padding = 8f;
    private const float SummaryTextWidth = 180f;
    private const float SummaryLineHeight = 18f;
    private const int SummaryLineCount = 2;
    private const float SummaryHeight = SummaryLineHeight * SummaryLineCount > 48 ? SummaryLineHeight * SummaryLineCount : 48f;

    /// <summary>Only 2, not SecondaryInventoryWindow's 5 -- this grid is always in shop mode (MapViewState.OpenShopEntityId is set for as long as this window exists), so InventoryGridContent always builds ShopItemStackCells here, 4x CellSize's own width each (see InventoryGridContent.ShopCellWidthMultiplier); 2 of those is already a wider row than 5 plain cells.</summary>
    private const int GridColumns = 2;

    /// <summary>Wide enough for exactly GridColumns columns of the shop-mode cell width -- see SecondaryInventoryWindow.GridWidth's own doc comment for why this derives from InventoryGridContent's own constants rather than hand-duplicating them.</summary>
    private static readonly float GridWidth = GridColumns * (InventoryGridContent.CellSize.X * InventoryGridContent.ShopCellWidthMultiplier + InventoryGridContent.CellGap) + 10f;

    private readonly DirectComponentPool<DisplayTextComponent> _displayTextPool = componentManager.GetDirectPool<DisplayTextComponent>();

    private int _entityId;
    private Tooltip _hoverPopup = null!;
    private Action<int, Guid> _onItemSelected = static (_, _) => { };
    private Action<int, Guid> _onCompareRequested = static (_, _) => { };
    private CurrencyRowContent _currencyRowContent = null!;

    /// <summary>Must be called after CreateElement but before Initialize -- same contract SecondaryInventoryWindow.Configure follows.</summary>
    public void Configure(int entityId, Tooltip hoverPopup, Action<int, Guid> onItemSelected, Action<int, Guid> onCompareRequested)
    {
        _entityId = entityId;
        _hoverPopup = hoverPopup;
        _onItemSelected = onItemSelected;
        _onCompareRequested = onCompareRequested;

        // getSecondaryTargetEntityId always returns this window's own _entityId -- this window
        // *is* the currently-open shop for as long as it exists (see ShopWindowController.
        // OpenShop, which never has more than one open at once), so its own currency row's
        // context menu only ever offers "Take"/"Take All" -- both suppressed for a shop (see
        // CurrencyRowContent.BuildCurrencyContextMenu's own ShopComponent check).
        _currencyRowContent = new CurrencyRowContent(entityId, componentManager, world, contextMenuController, ElementPoolService, FontService, LabelRenderer, spriteSheetService, spriteRenderer, () => _entityId);
        SetFooterContent(_currencyRowContent, CurrencyRowContent.Height);
    }

    /// <summary>See SecondaryInventoryWindow.OnChildrenInitialized's own doc comment for why children are built here, not in Configure.</summary>
    protected override void OnChildrenInitialized()
    {
        var gridHeight = ComputeGridHeight();
        var finalSize = ComputeOuterSize(gridHeight);
        SetMinimumSize(finalSize);
        SetSize(finalSize);

        base.OnChildrenInitialized();

        BuildSummary();
        BuildGrid(gridHeight);
    }

    /// <summary>
    /// 5, not SecondaryInventoryWindow's 2 -- this window is deliberately the opposite shape from
    /// the loot window (2 wide x 5 tall here vs. 5 wide x 2 tall there): a ShopItemStackCell holds
    /// far more information per item (sprite, name, and price, not just an icon+quantity badge),
    /// so a shop reads better as a scannable vertical list than a wide grid. See
    /// SecondaryInventoryWindow.MinimumGridRows/ComputeGridHeight's own doc comments for the
    /// shared "always at least this many rows, items can be dragged in later" reasoning.
    /// </summary>
    private const int MinimumGridRows = 5;

    private float ComputeGridHeight()
    {
        var stackCount = componentManager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(_entityId);
        var rows = System.Math.Max(MinimumGridRows, (int)System.Math.Ceiling(stackCount / (double)GridColumns));
        return rows * (InventoryGridContent.CellSize.Y + InventoryGridContent.CellGap);
    }

    /// <summary>FooterHeight is added explicitly, on top of outerInsets -- see SecondaryInventoryWindow.ComputeOuterSize's own doc comment for why outerInsets alone (ContentSize already excludes FooterHeight) undersizes the window by exactly one footer's worth of height.</summary>
    private Vector2 ComputeOuterSize(float gridHeight)
    {
        var contentWidth = System.Math.Max(IconSize.X + Padding + SummaryTextWidth, GridWidth);
        var contentHeight = SummaryHeight + Padding + gridHeight;

        var outerInsets = CurrentSize - ContentSize;
        return new Vector2(contentWidth, contentHeight) + outerInsets + new Vector2(0, FooterHeight);
    }

    private void BuildSummary()
    {
        var icon = ElementPoolService.CreateElement<EntityIconElement>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = IconSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        icon.Configure(_entityId, IconSize);
        AddChild(icon);

        var textX = Padding + IconSize.X;
        var (name, description) = ResolveDisplayText(_entityId);
        AddSummaryLine(textX, 0, name);
        AddSummaryLine(textX, 1, description);
    }

    private void AddSummaryLine(float x, int lineIndex, string text)
    {
        var line = ElementPoolService.CreateElement<TextWindow>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(x, lineIndex * SummaryLineHeight), Size = new Vector2(SummaryTextWidth, SummaryLineHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = Color.White },
        });
        AddChild(line);
    }

    /// <summary>Built at its final height from the start -- see SecondaryInventoryWindow.BuildGrid's own doc comment.</summary>
    private void BuildGrid(float gridHeight)
    {
        var gridWindow = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(0, SummaryHeight + Padding),
                Size = new Vector2(GridWidth, gridHeight),
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
        });

        // See SecondaryInventoryWindow.BuildGrid's own doc comment -- same flush-content fix for the same clipped-bottom-row bug.
        gridWindow.ContentPadding = Vector2.Zero;

        gridWindow.SetContent(new InventoryGridContent(world, componentManager, itemCatalog, ElementPoolService, FontService, LabelRenderer, spriteSheetService, spriteRenderer, contextMenuController, _entityId, filterTag: null, _hoverPopup, () => _entityId, mapViewState, _onItemSelected, _onCompareRequested));
        AddChild(gridWindow);
    }

    private (string Name, string Description) ResolveDisplayText(int entityId) =>
        _displayTextPool.TryGetReadonly(entityId, out var displayText) ? (displayText.Name, displayText.Description) : ("Unknown", string.Empty);
}
