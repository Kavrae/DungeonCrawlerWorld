using Engine.Tags;
using Engine.Utilities;
using Game.Modules.Inventory;
using Microsoft.Xna.Framework;
using Presentation.UI.Content;
using Presentation.UI.Inventory;
using Workshop;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Builders.Items;

/// <summary>The Items tab: every item in the session's catalog in a searchable browser, and the selected one shown in Item Details, read-only.</summary>
/// <remarks>
/// Two top-level windows in the Base layer, laid out in the area under the tab strip: the browser (a search box over a
/// scrolling list of headed rows) on the left and Item Details on the right. Item Details shows a definition with no
/// stack behind it (ItemDetailsWindow.NoStackInstanceId), so it has no blocker and no Compare.
/// </remarks>
public sealed class ItemsTab(ItemCatalog itemCatalog, GameplayTagRegistry gameplayTags, int previewEntityId) : IIdeTab
{
    public const string TabTitle = "Items";

    private const float Margin = 8f;
    private const float BrowserWidth = 340f;
    private const float PreviewWidth = 460f;
    private const float SearchBoxHeight = 26f;
    private const float SearchGap = 4f;
    private const float RowHeight = 24f;
    private const float RowIconSize = 20f;
    private const float RowTextIndent = 28f;
    private const string SearchGhostText = "Search items";

    private static readonly int SearchDebounceFrames = GameTiming.FramesForSeconds(0.3f);
    private static readonly Color RowColor = WindowPalette.PanelContentColor;
    private static readonly Color SelectedRowColor = WindowPalette.HighlightColor;
    private static readonly Color HeadingColor = WindowPalette.ControlBackground;

    private readonly DebouncedTextFilter _searchFilter = new(SearchDebounceFrames);
    private readonly List<(ItemDefinition Item, Window Row)> _rows = [];

    private WorkshopTabContext _context = null!;
    private Window _browserWindow = null!;
    private TextBox _searchBox = null!;
    private Window _listWindow = null!;
    private ItemDetailsWindow _previewWindow = null!;
    private ItemDefinition? _selectedItem;
    private bool _isOpen;

    public string Title => TabTitle;

    public void Open(WorkshopTabContext context, Rectangle area)
    {
        _context = context;
        _isOpen = true;
        BuildBrowser(area);
        BuildPreview(area);
        RebuildRows();

        if (_selectedItem is null && _rows.Count > 0)
        {
            Select(_rows[0].Item);
        }
    }

    public void Resize(Rectangle area)
    {
        if (!_isOpen)
        {
            return;
        }

        _browserWindow.SetSize(BrowserSize(area));
        _searchBox.SetSize(new Vector2(_browserWindow.ContentSize.X, SearchBoxHeight));
        _listWindow.SetSize(ListSize());
        _previewWindow.SetRelativePosition(PreviewPosition(area));
        _previewWindow.SetMaximumSize(PreviewMaximumSize(area));
    }

    public void Close()
    {
        if (!_isOpen)
        {
            return;
        }

        _isOpen = false;
        _rows.Clear();
        _context.Layers.Remove(UiLayer.Base, _browserWindow);
        _context.Layers.Remove(UiLayer.Base, _previewWindow);
        _browserWindow.Close();
        _previewWindow.Close();
    }

    public void Update(GameTime gameTime)
    {
        if (_searchFilter.Update(_searchBox.OriginalText))
        {
            RebuildRows();
        }
    }

    private void BuildBrowser(Rectangle area)
    {
        var elementPool = _context.UiEngine.ElementPoolService;
        _browserWindow = elementPool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(area.X + Margin, area.Y + Margin), Size = BrowserSize(area), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, TitleText = $"Items ({itemCatalog.Count})", CanUserFocus = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        _browserWindow.Initialize();
        _context.Layers.Add(UiLayer.Base, _browserWindow);

        _searchBox = elementPool.CreateElement<TextBox>(_browserWindow, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(_browserWindow.ContentSize.X, SearchBoxHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.ControlBackground },
            Text = new TextOptions { TextColor = WindowPalette.ControlLabelTextColor },
        });
        _searchBox.GhostText = SearchGhostText;
        _browserWindow.AddChild(_searchBox);

        _listWindow = elementPool.CreateElement<Window>(_browserWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, SearchBoxHeight + SearchGap), Size = ListSize(), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        _browserWindow.AddChild(_listWindow);
    }

    private void BuildPreview(Rectangle area)
    {
        _previewWindow = _context.UiEngine.ElementPoolService.CreateElement<ItemDetailsWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = PreviewPosition(area), MaximumSize = PreviewMaximumSize(area), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "Item Details", ShowBorder = true, CanUserScrollVertical = true, CanUserFocus = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        if (_selectedItem is { } selectedItem)
        {
            _previewWindow.Configure(previewEntityId, ItemDetailsWindow.NoStackInstanceId, selectedItem, PreviewWidth);
        }

        _previewWindow.Initialize();
        _context.Layers.Add(UiLayer.Base, _previewWindow);
    }

    private void RebuildRows()
    {
        var elementPool = _context.UiEngine.ElementPoolService;
        elementPool.CloseAllChildren(_listWindow);
        _rows.Clear();

        var rowWidth = _listWindow.ContentSize.X;
        foreach (var section in ItemBrowserSections.Build(itemCatalog.Definitions, _searchFilter.AppliedText, gameplayTags))
        {
            var headingSize = new Vector2(rowWidth, RowHeight);
            var headingRow = elementPool.CreateElement<TextWindow>(_listWindow, new ElementOptions
            {
                Layout = new ElementLayoutOptions { Size = headingSize, MinimumSize = headingSize, MaximumSize = headingSize, DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = HeadingColor },
                Text = new TextOptions { Text = section.Heading, TextColor = WindowPalette.ControlLabelTextColor },
            });
            _listWindow.AddChild(headingRow);

            foreach (var item in section.Items)
            {
                var row = CreateItemRow(rowWidth, item);
                _listWindow.AddChild(row);
                _rows.Add((item, row));
            }
        }
    }

    /// <summary>A row container holding the item's icon and its name; a press on any of the three selects the item.</summary>
    private Window CreateItemRow(float width, ItemDefinition item)
    {
        var elementPool = _context.UiEngine.ElementPoolService;
        var rowSize = new Vector2(width, RowHeight);
        var row = elementPool.CreateElement<Window>(_listWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = rowSize, MinimumSize = rowSize, MaximumSize = rowSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = item == _selectedItem ? SelectedRowColor : RowColor },
        });
        row.ContentPadding = Vector2.Zero;

        var iconSize = new Vector2(RowIconSize, RowIconSize);
        var icon = elementPool.CreateElement<ItemIconElement>(row, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(4, (RowHeight - RowIconSize) / 2), Size = iconSize, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        icon.Configure(item.SpriteName, item.Glyph, item.GlyphColor, iconSize);
        row.AddChild(icon);

        var nameSize = new Vector2(width - RowTextIndent, RowHeight);
        var name = elementPool.CreateElement<TextWindow>(row, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(RowTextIndent, 0), Size = nameSize, MinimumSize = nameSize, MaximumSize = nameSize, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Text = new TextOptions { Text = item.Name, TextColor = WindowPalette.BodyTextColor },
        });
        row.AddChild(name);

        row.Clicked += _ => Select(item);
        icon.Clicked += _ => Select(item);
        name.Clicked += _ => Select(item);
        return row;
    }

    private void Select(ItemDefinition item)
    {
        _selectedItem = item;
        foreach (var (rowItem, row) in _rows)
        {
            row.SetContentColor(rowItem == item ? SelectedRowColor : RowColor);
        }

        _previewWindow.Configure(previewEntityId, ItemDetailsWindow.NoStackInstanceId, item, PreviewWidth);
    }

    private static Vector2 BrowserSize(Rectangle area) => new(BrowserWidth, area.Height - Margin * 2);

    private Vector2 ListSize() => new(_browserWindow.ContentSize.X, _browserWindow.ContentSize.Y - SearchBoxHeight - SearchGap);

    private static Vector2 PreviewPosition(Rectangle area) => new(area.X + Margin * 2 + BrowserWidth, area.Y + Margin);

    private static Vector2 PreviewMaximumSize(Rectangle area) => new(PreviewWidth, area.Height - Margin * 2);
}
