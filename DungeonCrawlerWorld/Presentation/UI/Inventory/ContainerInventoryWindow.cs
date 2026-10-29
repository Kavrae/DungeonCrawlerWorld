using Engine.ECS.Systems;
using Engine.Tags;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI.Content;
using Presentation.UI.Looting;
using UIEngine;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;
using UIEngine.UI.Chrome;

namespace Presentation.UI.Inventory;

/// <summary>Another entity's inventory beside the player's: a summary, a header with its stack count, a grid that grows and shrinks a row at a time, and its Gold/Credits.</summary>
/// <remarks>
/// Every position and size is explicit -- fixed offsets and a fixed column count -- never ambient ContentSize
/// propagation or auto-tiling, which jumble a window built by hand this way.
/// The window is exactly as wide as its grid's columns. The grid shows one row more than its cells fill (a full grid
/// always has an empty row to drop into), from MinimumGridRows to MaximumGridRows; it gains the row the moment its last
/// one fills and loses it once a row and one more cell have emptied, so the two mirror each other. Past
/// MaximumGridRows it scrolls, and widens by the scrollbar so no column is lost. Rows follow the cells shown -- identical
/// stacks share a cell -- while the header counts stacks, which is what an inventory's capacity limits. The player can't
/// resize it; the subclass says what the summary reads and how the grid is shaped.
/// </remarks>
public abstract class ContainerInventoryWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    UiRenderService renderService,
    InventoryServices inventoryServices,
    World world,
    ContextMenuController contextMenuController,
    MapViewState mapViewState,
    SimulationClock simulationClock)
    : Window(fontService, elementPoolService, renderService)
{
    private static readonly Vector2 IconSize = new(48, 48);
    private const float Padding = 8f;
    private const string UnlimitedCapacityText = "∞";
    private static readonly float RowHeight = InventoryGridContent.CellSize.Y + InventoryGridContent.CellGap;

    private TooltipController _tooltipController = null!;
    private Action<int, uint> _onItemSelected = static (_, _) => { };
    private Action<int, uint> _onCompareRequested = static (_, _) => { };
    private Window _gridWindow = null!;
    private InventoryGridContent _gridContent = null!;
    private TextWindow _gridHeader = null!;
    private int _gridRows;
    private bool _gridScrolls;
    private readonly KeyedCache<(int StackCount, int? Capacity), string> _stackCountText = new();

    /// <summary>One line of the summary or the grid header, at the default font size.</summary>
    private float TextLineHeight => TextWindow.SingleLineHeight(FontService.GetFont(FontChrome.DefaultFontSize));

    protected int EntityId { get; private set; }

    /// <summary>How many cells a grid row holds.</summary>
    protected abstract int GridColumns { get; }

    /// <summary>One cell's width: InventoryGridContent's own for the cell type this grid builds.</summary>
    protected abstract float GridCellWidth { get; }

    /// <summary>The fewest rows shown, however few items there are.</summary>
    protected abstract int MinimumGridRows { get; }

    /// <summary>The most rows shown before the grid scrolls.</summary>
    protected abstract int MaximumGridRows { get; }

    /// <summary>The most stacks the inventory can hold, shown after its count in the header; null for no limit, shown as UnlimitedCapacityText.</summary>
    protected abstract int? StackCapacity { get; }

    /// <summary>How many lines BuildSummaryLines returns.</summary>
    protected abstract int SummaryLineCount { get; }

    /// <summary>The summary's lines beside the icon, top to bottom -- built once, when the window opens.</summary>
    protected abstract IReadOnlyList<string> BuildSummaryLines();

    private float GridColumnsWidth => GridColumns * GridCellWidth + (GridColumns - 1) * InventoryGridContent.CellGap;

    private float GridWidth => GridColumnsWidth + (_gridScrolls ? WindowChrome.ScrollbarThickness : 0f);

    private float SummaryHeight => System.Math.Max(TextLineHeight * SummaryLineCount, IconSize.Y);

    private float GridTop => SummaryHeight + Padding + TextLineHeight;

    /// <summary>Must be called after CreateElement but before Initialize. Registers the currency row here, so FooterHeight is set before the first measure.</summary>
    public void Configure(int entityId, TooltipController tooltipController, Action<int, uint> onItemSelected, Action<int, uint> onCompareRequested)
    {
        EntityId = entityId;
        _tooltipController = tooltipController;
        _onItemSelected = onItemSelected;
        _onCompareRequested = onCompareRequested;
        _gridRows = 0;
        _gridScrolls = false;
        OnConfigured();

        // This window is the secondary target for as long as it exists, so its currency row only ever offers "Take".
        SetFooterContent(new CurrencyRowContent(entityId, inventoryServices, world, contextMenuController, ElementPoolService, () => EntityId), CurrencyRowContent.Height);
    }

    /// <summary>Per-entity setup a subclass keeps, run by Configure.</summary>
    protected virtual void OnConfigured()
    {
    }

    /// <summary>Sized for the minimum before any child is built (the footer host is made off this window's ContentSize), then fitted to the cells the grid built.</summary>
    protected override void OnChildrenInitialized()
    {
        _gridRows = MinimumGridRows;
        SetWindowSize();

        base.OnChildrenInitialized();

        BuildSummary();
        BuildGridHeader();
        BuildGrid();
        FitGridToCells();
        UpdateGridHeader();
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        FitGridToCells();
        UpdateGridHeader();
    }

    private void UpdateGridHeader() =>
        _gridHeader.UpdateText(_stackCountText.Get((inventoryServices.InventoryView.CountStacks(EntityId), StackCapacity), static key => key.Capacity is { } capacity ? $"{key.StackCount}/{capacity}" : $"{key.StackCount}/{UnlimitedCapacityText}"));

    /// <summary>Resizes the window and its grid when the cells shown need another row or leave one spare -- never otherwise.</summary>
    private void FitGridToCells()
    {
        var cellCount = _gridContent.VisibleItemCount;
        var rows = System.Math.Clamp(cellCount / GridColumns + 1, MinimumGridRows, MaximumGridRows);
        var scrolls = cellCount > MaximumGridRows * GridColumns;
        if (rows == _gridRows && scrolls == _gridScrolls)
        {
            return;
        }

        _gridRows = rows;
        _gridScrolls = scrolls;
        SetWindowSize();
        _gridWindow.SetBounds(new Vector2(0, GridTop), new Vector2(GridWidth, rows * RowHeight));
        _gridHeader.SetBounds(new Vector2(0, SummaryHeight + Padding), new Vector2(GridWidth, TextLineHeight));
    }

    /// <summary>The content area holds the summary, header and grid at their current size; FooterHeight is added on top, since ContentSize already excludes it.</summary>
    private void SetWindowSize()
    {
        var outerInsets = CurrentSize - ContentSize;
        var size = new Vector2(GridWidth, GridTop + _gridRows * RowHeight) + outerInsets + new Vector2(0, FooterHeight);
        SetMinimumSize(size);
        SetSize(size);
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
        icon.Configure(EntityId, IconSize);
        AddChild(icon);

        var textX = IconSize.X + Padding;
        var lines = BuildSummaryLines();
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            AddChild(CreateTextLine(new Vector2(textX, lineIndex * TextLineHeight), new Vector2(GridColumnsWidth - textX, TextLineHeight), lines[lineIndex]));
        }
    }

    /// <summary>The line above the grid: the stack count on the left, room on the right for what an inventory will also show (weight).</summary>
    private void BuildGridHeader()
    {
        _gridHeader = CreateTextLine(new Vector2(0, SummaryHeight + Padding), new Vector2(GridWidth, TextLineHeight), string.Empty);
        AddChild(_gridHeader);
    }

    private TextWindow CreateTextLine(Vector2 position, Vector2 size, string text) =>
        ElementPoolService.CreateElement<TextWindow>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = Color.White },
        });

    private void BuildGrid()
    {
        _gridWindow = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, GridTop), Size = new Vector2(GridWidth, _gridRows * RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
        });

        // Flush against the grid window's bounds: the rows are sized to its cells, with no padding budgeted in.
        _gridWindow.ContentPadding = Vector2.Zero;

        // Activate is player-inventory-only (InventoryGridContent.CanActivate), so it is never reached here.
        _gridContent = new InventoryGridContent(world, inventoryServices, ElementPoolService, contextMenuController, EntityId, filterTag: GameplayTag.None, _tooltipController, () => EntityId, mapViewState, _onItemSelected, _onCompareRequested, static (_, _) => { }, static _ => { }, simulationClock: simulationClock);
        _gridWindow.SetContent(_gridContent);
        AddChild(_gridWindow);
    }
}
