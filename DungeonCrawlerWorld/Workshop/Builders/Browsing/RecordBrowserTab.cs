using Engine.Utilities;
using Microsoft.Xna.Framework;
using Presentation.UI.Content;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;
using Workshop;

namespace Builders.Browsing;

/// <summary>How a record's row reads in the browser: its icon and its name.</summary>
public readonly record struct RecordRowAppearance(string Name, string? SpriteName, string Glyph, Color GlyphColor);

/// <summary>A read-only tab over one table: every record in a searchable browser, the selected one shown in a details window, and what it is linked to.</summary>
/// <remarks>
/// Top-level windows in the Base layer, laid out in the area under the tab strip: the browser (a search box over a
/// scrolling list of headed rows) on the left, the preview (TPreview, WrapContent) beside it, an optional details pane
/// (CreateDetailsWindow) beside that, and Related (LinksOf: what the record uses and what uses it) last. A Related line
/// that names a record another tab shows asks the shell's TabNavigator to open it there. Selection is by reference, never record equality. The first record is selected the
/// first time the tab opens; reopening keeps the selection; TrySelectRecord selects one by id, even one the search hides.
/// </remarks>
public abstract class RecordBrowserTab<TRecord, TPreview> : IIdeTab
    where TRecord : class
    where TPreview : Window
{
    private const float Margin = 8f;
    private const float BrowserWidth = 340f;
    private const float SearchBoxHeight = 26f;
    private const float SearchGap = 4f;
    private const float RowHeight = 24f;
    private const float RowIconSize = 20f;
    private const float RowTextIndent = 28f;

    /// <summary>The width every preview row is built to by default -- Item Details' own width beside the player's Inventory window.</summary>
    protected const float DefaultPreviewWidth = 460f;

    /// <summary>The width the preview is built to; a tab whose preview needs more room (a map) widens it.</summary>
    protected virtual float PreviewWidth => DefaultPreviewWidth;

    private const float RelatedWidth = 320f;
    private const string RelatedTitle = "Related";
    private const float DefaultDetailsWidth = 360f;
    private const string NoLinksText = "None";

    private static readonly int SearchDebounceFrames = GameTiming.FramesForSeconds(0.3f);
    private static readonly Color RowColor = WindowPalette.PanelContentColor;
    private static readonly Color SelectedRowColor = WindowPalette.HighlightColor;
    private static readonly Color HeadingColor = WindowPalette.ControlBackground;
    private static readonly Color LinkTextColor = Color.LightSkyBlue;
    private static readonly Color PlainLinkTextColor = WindowPalette.TitleTextColor;

    private readonly DebouncedTextFilter _searchFilter = new(SearchDebounceFrames);
    private readonly List<(TRecord Record, Window Row)> _rows = [];

    private WorkshopTabContext _context = null!;
    private Window _browserWindow = null!;
    private TextBox _searchBox = null!;
    private Window _listWindow = null!;
    private TPreview _previewWindow = null!;
    private Window _relatedWindow = null!;
    private Window? _detailsWindow;
    private TRecord? _selectedRecord;
    private bool _isOpen;

    public abstract string Title { get; }

    /// <summary>The browser window's title, e.g. "Items (13)".</summary>
    protected abstract string BrowserTitle { get; }

    protected abstract string SearchGhostText { get; }

    protected abstract string PreviewTitle { get; }

    protected abstract IReadOnlyList<RecordBrowserSection<TRecord>> BuildSections(string search);

    protected abstract RecordRowAppearance AppearanceOf(TRecord record);

    /// <summary>Shows record in the preview, every row built to contentWidth.</summary>
    protected abstract void ShowInPreview(TPreview preview, TRecord record, float contentWidth);

    /// <summary>Every record the tab shows, whatever the search -- what TrySelectRecord looks an id up in.</summary>
    protected abstract IEnumerable<TRecord> AllRecords { get; }

    protected abstract Guid IdOf(TRecord record);

    /// <summary>The Related window's sections for record -- none by default.</summary>
    protected virtual IReadOnlyList<RecordLinkSection> LinksOf(TRecord record) => [];

    /// <summary>What names record, for the bottom panel's Dependents -- nothing by default.</summary>
    protected virtual IReadOnlyList<WorkshopDependent> DependentsOf(TRecord record) => [];

    /// <summary>The open tab's preview window.</summary>
    protected TPreview PreviewWindow => _previewWindow;

    /// <summary>The open tab's details pane, if CreateDetailsWindow made one.</summary>
    protected Window? DetailsWindow => _detailsWindow;

    /// <summary>The preview's title while record is selected -- PreviewTitle by default.</summary>
    protected virtual string PreviewTitleFor(TRecord record) => PreviewTitle;

    /// <summary>The details pane's title while record is selected -- DetailsTitle by default.</summary>
    protected virtual string DetailsTitleFor(TRecord record) => DetailsTitle;

    /// <summary>Called when record is selected, once every window shows it.</summary>
    protected virtual void OnSelected(TRecord record)
    {
    }

    /// <summary>Called as the tab closes, before its own windows are removed -- where a tab removes windows of its own.</summary>
    protected virtual void OnClosing()
    {
    }

    /// <summary>The width of the details pane, for a tab that has one (CreateDetailsWindow).</summary>
    protected virtual float DetailsWidth => DefaultDetailsWidth;

    protected virtual string DetailsTitle => "Details";

    /// <summary>A details pane between the preview and Related, made from options with the pool -- or null, the default, for none.</summary>
    protected virtual Window? CreateDetailsWindow(ElementPoolService elementPool, ElementOptions options) => null;

    /// <summary>Shows record in the details pane CreateDetailsWindow made, every row built to contentWidth.</summary>
    protected virtual void ShowInDetails(Window details, TRecord record, float contentWidth)
    {
    }

    /// <summary>Called once the tab's windows are built, before the first record is shown -- where a tab registers its own input handlers through context.</summary>
    protected virtual void OnOpened(WorkshopTabContext context)
    {
    }

    /// <summary>Called when the tab's area changes, after every window has its new position and maximum size.</summary>
    protected virtual void OnResized(Rectangle area)
    {
    }

    /// <summary>Per-frame work of the tab's own, after the search.</summary>
    protected virtual void OnUpdate(GameTime gameTime)
    {
    }

    public void Open(WorkshopTabContext context, Rectangle area)
    {
        _context = context;
        _isOpen = true;
        BuildBrowser(area);
        BuildPreview(area);
        BuildDetails(area);
        BuildRelated(area);
        RebuildRows();
        OnOpened(context);

        if (_selectedRecord is { } selectedRecord)
        {
            RebuildRelated(selectedRecord);
            ShowDependents(selectedRecord);
            ShowTitles(selectedRecord);
            OnSelected(selectedRecord);
        }
        else if (_rows.Count > 0)
        {
            Select(_rows[0].Record);
        }

        ArrangeColumns();
    }

    public bool TrySelectRecord(Guid recordId)
    {
        if (!_isOpen)
        {
            return false;
        }

        foreach (var record in AllRecords)
        {
            if (IdOf(record) == recordId)
            {
                Select(record);
                return true;
            }
        }

        return false;
    }

    public void Resize(Rectangle area)
    {
        if (!_isOpen)
        {
            return;
        }

        _browserWindow.SetMaximumSize(BrowserSize(area));
        _browserWindow.SetSize(BrowserSize(area));
        _searchBox.SetMaximumSize(new Vector2(_browserWindow.ContentSize.X, SearchBoxHeight));
        _searchBox.SetSize(new Vector2(_browserWindow.ContentSize.X, SearchBoxHeight));
        _listWindow.SetMaximumSize(ListSize());
        _listWindow.SetSize(ListSize());
        _previewWindow.SetRelativePosition(PreviewPosition(area));
        _previewWindow.SetMaximumSize(PreviewMaximumSize(area));
        _detailsWindow?.SetRelativePosition(DetailsPosition(area));
        _detailsWindow?.SetMaximumSize(DetailsMaximumSize(area));
        _relatedWindow.SetRelativePosition(RelatedPosition(area));
        _relatedWindow.SetMaximumSize(RelatedMaximumSize(area));
        OnResized(area);
        ArrangeColumns();
    }

    public void Close()
    {
        if (!_isOpen)
        {
            return;
        }

        OnClosing();
        _isOpen = false;
        _rows.Clear();
        _context.Layers.Remove(UiLayer.Base, _browserWindow);
        _context.Layers.Remove(UiLayer.Base, _previewWindow);
        _context.Layers.Remove(UiLayer.Base, _relatedWindow);
        if (_detailsWindow is { } detailsWindow)
        {
            _context.Layers.Remove(UiLayer.Base, detailsWindow);
            detailsWindow.Close();
            _detailsWindow = null;
        }

        _browserWindow.Close();
        _previewWindow.Close();
        _relatedWindow.Close();
    }

    public void Update(GameTime gameTime)
    {
        if (_searchFilter.Update(_searchBox.OriginalText))
        {
            RebuildRows();
        }

        ArrangeColumns();
        OnUpdate(gameTime);
    }

    private void BuildBrowser(Rectangle area)
    {
        var elementPool = _context.UiEngine.ElementPoolService;
        _browserWindow = elementPool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(area.X + Margin, area.Y + Margin), Size = BrowserSize(area), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, TitleText = BrowserTitle, CanUserFocus = true },
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
        _previewWindow = _context.UiEngine.ElementPoolService.CreateElement<TPreview>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = PreviewPosition(area), MaximumSize = PreviewMaximumSize(area), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = PreviewTitle, ShowBorder = true, CanUserScrollVertical = true, CanUserFocus = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        if (_selectedRecord is { } selectedRecord)
        {
            ShowInPreview(_previewWindow, selectedRecord, PreviewWidth);
        }

        _previewWindow.Initialize();
        _context.Layers.Add(UiLayer.Base, _previewWindow);
    }

    private void BuildDetails(Rectangle area)
    {
        _detailsWindow = CreateDetailsWindow(_context.UiEngine.ElementPoolService, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = DetailsPosition(area), MaximumSize = DetailsMaximumSize(area), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = DetailsTitle, ShowBorder = true, CanUserScrollVertical = true, CanUserFocus = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        if (_detailsWindow is not { } detailsWindow)
        {
            return;
        }

        if (_selectedRecord is { } selectedRecord)
        {
            ShowInDetails(detailsWindow, selectedRecord, DetailsWidth);
        }

        detailsWindow.Initialize();
        _context.Layers.Add(UiLayer.Base, detailsWindow);
    }

    private void BuildRelated(Rectangle area)
    {
        _relatedWindow = _context.UiEngine.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = RelatedPosition(area), MaximumSize = RelatedMaximumSize(area), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = RelatedTitle, ShowBorder = true, CanUserScrollVertical = true, CanUserFocus = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        _relatedWindow.Initialize();
        _context.Layers.Add(UiLayer.Base, _relatedWindow);
    }

    /// <summary>One heading row per section and one line per link under it ("None" for a section with none); a line that opens a record is drawn as a link and asks the navigator when clicked.</summary>
    private void RebuildRelated(TRecord record)
    {
        var elementPool = _context.UiEngine.ElementPoolService;
        elementPool.CloseAllChildren(_relatedWindow);

        var rowWidth = RelatedWidth - _relatedWindow.ContentPadding.X * 2;
        foreach (var section in LinksOf(record))
        {
            _relatedWindow.AddChild(CreateRelatedRow(rowWidth, section.Heading, WindowPalette.ControlLabelTextColor, HeadingColor));

            if (section.Links.Count == 0)
            {
                _relatedWindow.AddChild(CreateRelatedRow(rowWidth, NoLinksText, PlainLinkTextColor, Color.Transparent));
                continue;
            }

            foreach (var link in section.Links)
            {
                var row = CreateRelatedRow(rowWidth, link.Text, link.OpensARecord ? LinkTextColor : PlainLinkTextColor, Color.Transparent);
                if (link.TargetTabTitle is { } targetTabTitle)
                {
                    var targetId = link.TargetId;
                    row.Clicked += _ => _context.Navigator.RequestOpenRecord(targetTabTitle, targetId);
                }

                _relatedWindow.AddChild(row);
            }
        }
    }

    private TextWindow CreateRelatedRow(float width, string text, Color textColor, Color backgroundColor)
    {
        var size = new Vector2(width, RowHeight);
        return _context.UiEngine.ElementPoolService.CreateElement<TextWindow>(_relatedWindow, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = size, MinimumSize = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = backgroundColor },
            Text = new TextOptions { Text = text, TextColor = textColor },
        });
    }

    private void RebuildRows()
    {
        var elementPool = _context.UiEngine.ElementPoolService;
        elementPool.CloseAllChildren(_listWindow);
        _rows.Clear();

        var rowWidth = _listWindow.ContentSize.X;
        foreach (var section in BuildSections(_searchFilter.AppliedText))
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

            foreach (var record in section.Records)
            {
                var row = CreateRecordRow(rowWidth, record);
                _listWindow.AddChild(row);
                _rows.Add((record, row));
            }
        }
    }

    /// <summary>A row container holding the record's icon and its name; a click on any of them reaches the row, which selects the record.</summary>
    private Window CreateRecordRow(float width, TRecord record)
    {
        var elementPool = _context.UiEngine.ElementPoolService;
        var appearance = AppearanceOf(record);
        var rowSize = new Vector2(width, RowHeight);
        var row = elementPool.CreateElement<Window>(_listWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = rowSize, MinimumSize = rowSize, MaximumSize = rowSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = ReferenceEquals(record, _selectedRecord) ? SelectedRowColor : RowColor },
        });
        row.ContentPadding = Vector2.Zero;

        var iconSize = new Vector2(RowIconSize, RowIconSize);
        var icon = elementPool.CreateElement<ItemIconElement>(row, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(4, (RowHeight - RowIconSize) / 2), Size = iconSize, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        icon.Configure(appearance.SpriteName, appearance.Glyph, appearance.GlyphColor, iconSize);
        row.AddChild(icon);

        var nameSize = new Vector2(width - RowTextIndent, RowHeight);
        var name = elementPool.CreateElement<TextWindow>(row, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(RowTextIndent, 0), Size = nameSize, MinimumSize = nameSize, MaximumSize = nameSize, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Text = new TextOptions { Text = appearance.Name, TextColor = WindowPalette.BodyTextColor },
        });
        row.AddChild(name);

        row.Clicked += _ => Select(record);
        return row;
    }

    private void Select(TRecord record)
    {
        _selectedRecord = record;
        foreach (var (rowRecord, row) in _rows)
        {
            row.SetContentColor(ReferenceEquals(rowRecord, record) ? SelectedRowColor : RowColor);
        }

        ShowInPreview(_previewWindow, record, PreviewWidth);
        if (_detailsWindow is { } detailsWindow)
        {
            ShowInDetails(detailsWindow, record, DetailsWidth);
        }

        RebuildRelated(record);
        ShowDependents(record);
        ShowTitles(record);
        ArrangeColumns();
        OnSelected(record);
    }

    private void ShowDependents(TRecord record) => _context.ShowDependents(AppearanceOf(record).Name, DependentsOf(record));

    private void ShowTitles(TRecord record)
    {
        _previewWindow.TitleText = PreviewTitleFor(record);
        if (_detailsWindow is { } detailsWindow)
        {
            detailsWindow.TitleText = DetailsTitleFor(record);
        }
    }

    /// <summary>Places the details pane and Related each a margin right of the actual right edge of the column before it: a window that wraps its content can be wider than the width it was built to.</summary>
    private void ArrangeColumns()
    {
        var right = _previewWindow.RelativePosition.X + _previewWindow.CurrentSize.X + Margin;
        if (_detailsWindow is { } detailsWindow)
        {
            MoveColumnTo(detailsWindow, right);
            right = detailsWindow.RelativePosition.X + detailsWindow.CurrentSize.X + Margin;
        }

        MoveColumnTo(_relatedWindow, right);
    }

    private static void MoveColumnTo(Window column, float x)
    {
        if (column.RelativePosition.X != x)
        {
            column.SetRelativePosition(new Vector2(x, column.RelativePosition.Y));
        }
    }

    private static Vector2 BrowserSize(Rectangle area) => new(BrowserWidth, area.Height - Margin * 2);

    private Vector2 ListSize() => new(_browserWindow.ContentSize.X, _browserWindow.ContentSize.Y - SearchBoxHeight - SearchGap);

    private static Vector2 PreviewPosition(Rectangle area) => new(area.X + Margin * 2 + BrowserWidth, area.Y + Margin);

    private Vector2 PreviewMaximumSize(Rectangle area) => new(PreviewWidth, area.Height - Margin * 2);

    private Vector2 DetailsPosition(Rectangle area) => PreviewPosition(area) + new Vector2(PreviewWidth + Margin, 0);

    private Vector2 DetailsMaximumSize(Rectangle area) => new(DetailsWidth, area.Height - Margin * 2);

    private Vector2 RelatedPosition(Rectangle area) => _detailsWindow is null ? DetailsPosition(area) : DetailsPosition(area) + new Vector2(DetailsWidth + Margin, 0);

    private static Vector2 RelatedMaximumSize(Rectangle area) => new(RelatedWidth, area.Height - Margin * 2);
}
