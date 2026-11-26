using Engine.ECS.Systems;
using Engine.Tags;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI.Content;
using Presentation.UI.Looting;
using UIEngine;
using UIEngine.Fonts;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;
using UIEngine.UI.Chrome;

namespace Presentation.UI.Inventory;

/// <summary>Another entity's inventory beside the player's: a summary, a header with its stack count, a grid that grows and shrinks a row at a time, and its Gold/Credits.</summary>
/// <remarks>
/// Every position and size is explicit -- fixed offsets and a fixed column count -- never ambient ContentSize
/// propagation or auto-tiling, which jumble a window built by hand this way.
/// The window is exactly as wide as its grid's columns, unless it has a FixedWindowWidth. The grid shows one row more than its cells fill (a full grid
/// always has an empty row to drop into), from MinimumGridRows to MaximumGridRows; it gains the row the moment its last
/// one fills and loses it once a row and one more cell have emptied, so the two mirror each other. Past
/// MaximumGridRows it scrolls, and widens by the scrollbar so no column is lost. Rows follow the cells shown -- identical
/// stacks share a cell -- while the header counts stacks, which is what an inventory's capacity limits. The player can't
/// resize it; the subclass says what the summary reads and how the grid is shaped.
/// </remarks>
public abstract class ContainerInventoryWindow(
    ElementServices services,
    InventoryServices inventoryServices,
    World world,
    ContextMenuController contextMenuController,
    MapViewState mapViewState)
    : Window(services)
{
    private static readonly Vector2 IconSize = new(48, 48);
    private const float Padding = 8f;
    private const string UnlimitedCapacityText = "∞";
    private static readonly float RowHeight = InventoryGridContent.CellSize.Y + InventoryGridContent.CellGap;

    private Action<int, uint> _onItemSelected = static (_, _) => { };
    private Window _gridWindow = null!;
    private InventoryGridContent _gridContent = null!;
    private TextWindow _gridHeader = null!;
    private int _gridRows;
    private int _gridColumns;
    private bool _gridScrolls;
    private float _summaryHeight;
    private CurrencyRowContent _currencyRow = null!;
    private readonly KeyedCache<(int StackCount, int? Capacity), string> _stackCountText = new();

    /// <summary>One line of the summary or the grid header, at the default font size.</summary>
    private float TextLineHeight => TextWindow.SingleLineHeight(FontService.GetFont(FontChrome.DefaultFontSize));

    protected int EntityId { get; private set; }

    /// <summary>What every item window reads and does, for a subclass that needs more of it than this class's own grid and currency row.</summary>
    protected InventoryServices InventoryServices => inventoryServices;

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

    /// <summary>The window's width when it is set rather than fitted to its grid's columns (the shop's: two cards and a scrollbar); the grid keeps its own width at the left.</summary>
    protected virtual float? FixedWindowWidth => null;

    /// <summary>The view this window's grid shows, switched elsewhere (the shop's, from the player's Inventory window); null for a grid always in Icon view (the loot window).</summary>
    protected virtual GridViewSetting? GridViewSetting => null;

    /// <summary>The summary's lines beside the icon, top to bottom -- built once, when the window opens; each wraps and pushes what follows down.</summary>
    protected abstract IReadOnlyList<string> BuildSummaryLines();

    private float GridColumnsWidth => GridColumns * GridCellWidth + (GridColumns - 1) * InventoryGridContent.CellGap;

    private float GridWidth => GridColumnsWidth + (_gridScrolls ? WindowChrome.ScrollbarThickness : 0f);

    private float GridTop => _summaryHeight + Padding + TextLineHeight;

    /// <summary>Must be called after CreateElement but before Initialize. Registers the currency row here, so FooterHeight is set before the first measure.</summary>
    public void Configure(int entityId, Action<int, uint> onItemSelected)
    {
        EntityId = entityId;
        _onItemSelected = onItemSelected;
        _gridRows = 0;
        _gridColumns = 0;
        _gridScrolls = false;
        _summaryHeight = IconSize.Y;
        OnConfigured();

        // This window is the secondary target for as long as it exists, so its currency row only ever offers "Take".
        var footerButton = BuildFooterButton();
        _currencyRow = new CurrencyRowContent(entityId, inventoryServices, world, contextMenuController, ElementPoolService, () => EntityId, showLabels: footerButton is null, rowButton: footerButton);
        SetFooterContent(_currencyRow, _currencyRow.RequiredHeight);
    }

    /// <summary>The button beside the currency amounts, if this window has one; the amounts drop their labels to make room.</summary>
    protected virtual CurrencyRowButton? BuildFooterButton() => null;

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
        FitFooterToCurrency();
        FitGridToCells();
        UpdateGridHeader();
    }

    private void FitFooterToCurrency()
    {
        var requiredHeight = _currencyRow.RequiredHeight;
        if (requiredHeight != FooterHeight)
        {
            ResizeFooter(requiredHeight);
        }
    }

    private void UpdateGridHeader() =>
        _gridHeader.UpdateText(_stackCountText.Get((inventoryServices.InventoryView.CountStacks(EntityId), StackCapacity), static key => key.Capacity is { } capacity ? $"{key.StackCount}/{capacity}" : $"{key.StackCount}/{UnlimitedCapacityText}"));

    /// <summary>Resizes the window and its grid when the cells shown need another row or leave one spare, or the view changes its column count -- never otherwise.</summary>
    private void FitGridToCells()
    {
        var cellCount = _gridContent.VisibleItemCount;
        var rows = System.Math.Clamp(cellCount / GridColumns + 1, MinimumGridRows, MaximumGridRows);
        var scrolls = cellCount > MaximumGridRows * GridColumns;
        if (rows == _gridRows && scrolls == _gridScrolls && GridColumns == _gridColumns)
        {
            return;
        }

        _gridColumns = GridColumns;
        _gridRows = rows;
        _gridScrolls = scrolls;
        SetWindowSize();
        _gridWindow.SetBounds(new Vector2(0, GridTop), new Vector2(GridWidth, rows * RowHeight));
        _gridHeader.SetBounds(new Vector2(0, _summaryHeight + Padding), new Vector2(GridWidth, TextLineHeight));
    }

    /// <summary>The content area holds the summary, header and grid at their current size; ContentInset adds the chrome and the footer.</summary>
    private void SetWindowSize()
    {
        var size = new Vector2(GridWidth, GridTop + _gridRows * RowHeight) + ContentInset;
        if (FixedWindowWidth is { } width)
        {
            size.X = width;
        }

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
        var textWidth = GridColumnsWidth - textX;
        var textLines = new List<(TextWindow Line, float Height)>();
        var textHeight = 0f;
        foreach (var text in BuildSummaryLines())
        {
            var line = CreateTextLine(new Vector2(textX, textHeight), new Vector2(textWidth, TextLineHeight), text);
            AddChild(line);
            var lineHeight = TextLineHeight + (line.DisplayText.LineCount - 1) * line.ContentFont.LineHeight;
            textLines.Add((line, lineHeight));
            textHeight += lineHeight;
        }

        _summaryHeight = System.Math.Max(textHeight, IconSize.Y);
        SetWindowSize();

        var lineTop = 0f;
        foreach (var (line, lineHeight) in textLines)
        {
            line.SetBounds(new Vector2(textX, lineTop), new Vector2(textWidth, lineHeight));
            lineTop += lineHeight;
        }
    }

    /// <summary>The line above the grid: the stack count on the left, room on the right for what an inventory will also show (weight).</summary>
    private void BuildGridHeader()
    {
        _gridHeader = CreateTextLine(new Vector2(0, _summaryHeight + Padding),new Vector2(GridWidth, TextLineHeight), string.Empty);
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
        _gridContent = new InventoryGridContent(world, inventoryServices, ElementPoolService, contextMenuController, EntityId, filterTag: GameplayTag.None, () => EntityId, mapViewState, _onItemSelected, static (_, _) => { }, static _ => { }, viewSetting: GridViewSetting);
        _gridWindow.SetContent(_gridContent);
        AddChild(_gridWindow);
    }
}
