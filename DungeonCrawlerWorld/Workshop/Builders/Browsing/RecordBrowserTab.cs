using Engine.Utilities;
using Microsoft.Xna.Framework;
using Presentation.UI.Content;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;
using UIEngine.UI.Fields;
using Workshop;

namespace Builders.Browsing;

/// <summary>How a record's row reads in the browser: its icon and its name, greyed when the record is out of play (disabled).</summary>
public readonly record struct RecordRowAppearance(string Name, string? SpriteName, string Glyph, Color GlyphColor)
{
    public bool IsDimmed { get; init; }
}

/// <summary>Something a tab does to one record (Publish, Delete): a button over the inspector and an option on the record's right-click menu, both running Run; Tooltip says what it does, or why it can't.</summary>
public sealed record RecordCommand(string Label, string Tooltip, bool IsEnabled, Action Run);

/// <summary>A switch in how a tab shows its records ("Changes only"): a checkbox at the right end of the command bar, never a command on a record; IsOn reads its state, Changed sets it.</summary>
public sealed record RecordViewOption(string Label, string Tooltip, Func<bool> IsOn, Action<bool> Changed);

/// <summary>A read-only tab over one table: every record in a searchable browser, the selected one shown in a details window, and what it is linked to.</summary>
/// <remarks>
/// Top-level windows in the Base layer, laid out in the area under the tab strip: the browser (a search box over a
/// scrolling list of headed rows) on the left, the preview (TPreview, WrapContent) beside it, an optional details pane
/// (CreateDetailsWindow) beside that. What the selected record uses and what uses it go to the bottom panel's Uses and
/// Used By (UsesOf, UsedByOf). Selection is by reference, never record equality. The first record is selected the
/// first time the tab opens; reopening keeps the selection, the search and the selected row in view; TrySelectRecord
/// selects one by id, even one the search hides. A tab that makes records (NewRecordLabel) has a button for it right of
/// the search box, which selects what it made.
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

    private const float DefaultDetailsWidth = 360f;

    private static readonly int SearchDebounceFrames = GameTiming.FramesForSeconds(0.3f);
    private static readonly Color RowColor = WindowPalette.PanelContentColor;
    private static readonly Color SelectedRowColor = WindowPalette.ListSelectionColor;
    private static readonly Color HeadingColor = WindowPalette.ControlBackground;
    private static readonly Color DimmedRowTextColor = WindowPalette.DimmedRowTextColor;

    private readonly DebouncedTextFilter _searchFilter = new(SearchDebounceFrames);
    private readonly List<(TRecord Record, Window Row)> _rows = [];

    private const float MinimumInspectorWidth = 300f;
    private const float RecordProblemsHeight = 52f;
    private const string InspectorTitle = "Inspector";

    private const float NewRecordButtonWidth = 56f;
    private const float CommandBarHeight = 26f;
    private const float CommandButtonPadding = 16f;
    private const float CommandButtonGap = 4f;

    /// <summary>Room between a view switch's checkbox and its label, and after the label.</summary>
    private const float ToggleLabelGap = 10f;

    private readonly List<Toggle> _viewOptionToggles = [];

    private WorkshopTabContext _context = null!;
    private Window _browserWindow = null!;
    private TextBox _searchBox = null!;
    private Button? _newRecordButton;
    private bool _newRecordRequested;
    private Window _listWindow = null!;
    private TPreview _previewWindow = null!;
    private Window? _detailsWindow;
    private Window? _inspectorWindow;
    private TextWindow? _recordProblemsWindow;
    private Window? _commandBarWindow;
    private Action? _pendingCommand;
    private TRecord? _selectedRecord;
    private Rectangle _area;
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

    /// <summary>What names record, for the bottom panel's Used By -- nothing by default.</summary>
    protected virtual IReadOnlyList<WorkshopRelatedRecord> UsedByOf(TRecord record) => [];

    /// <summary>What record names, for the bottom panel's Uses -- nothing by default.</summary>
    protected virtual IReadOnlyList<WorkshopRelatedRecord> UsesOf(TRecord record) => [];

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

    /// <summary>A details pane beside the preview, made from options with the pool -- or null, the default, for none.</summary>
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

    /// <summary>Whether the tab edits its records: an inspector column between the browser and the preview.</summary>
    protected virtual bool HasInspector => false;

    /// <summary>The label of the button right of the search box that makes a new record; null, the default, for no button.</summary>
    protected virtual string? NewRecordLabel => null;

    protected virtual string? NewRecordTooltip => null;

    /// <summary>Makes a new record for the New button, returning it to be selected; null when none was made.</summary>
    protected virtual TRecord? CreateNewRecord() => null;

    /// <summary>What can be done to record: buttons over the inspector (a tab with HasInspector) and its row's right-click menu -- none by default.</summary>
    protected virtual IReadOnlyList<RecordCommand> CommandsFor(TRecord record) => [];

    /// <summary>The tab's view switches, as checkboxes at the right end of the command bar -- none by default.</summary>
    protected virtual IReadOnlyList<RecordViewOption> ViewOptions => [];

    /// <summary>Called when record is selected or shown again, with the inspector column's window to fill -- for a tab with HasInspector.</summary>
    protected virtual void ShowInInspector(Window inspector, TRecord record)
    {
    }

    /// <summary>The open tab's context, for a tab that focuses or navigates on its own.</summary>
    protected WorkshopTabContext Context => _context;

    protected bool IsOpen => _isOpen;

    /// <summary>The record selected, or null before the first.</summary>
    protected TRecord? SelectedRecord => _selectedRecord;

    /// <summary>Shows what is wrong with the selected record above the inspector (nothing hides the strip); a tab with HasInspector only.</summary>
    protected void ShowRecordProblems(string? text)
    {
        if (_recordProblemsWindow is not { } strip || _inspectorWindow is not { } inspector)
        {
            return;
        }

        var isShown = !string.IsNullOrEmpty(text);
        strip.UpdateText(text ?? string.Empty);
        if (strip.IsVisible == isShown)
        {
            return;
        }

        strip.IsVisible = isShown;
        LayOutInspector(_area);
    }

    /// <summary>Builds the buttons over the inspector again for the selected record -- after what it can do changed (a save).</summary>
    protected void RefreshCommands()
    {
        if (_isOpen && _selectedRecord is { } record)
        {
            RebuildCommandBar(record);
        }
    }

    /// <summary>One button per command of record, each running it on the next Update, so nothing it rebuilds is the button being clicked.</summary>
    private void RebuildCommandBar(TRecord record)
    {
        if (_commandBarWindow is not { } bar)
        {
            return;
        }

        var elementPool = _context.UiEngine.ElementPoolService;
        elementPool.CloseAllChildren(bar);
        var font = _context.UiEngine.FontService.GetFont(UIEngine.UI.Chrome.FontChrome.DefaultFontSize);
        var x = 0f;
        foreach (var command in CommandsFor(record))
        {
            var width = font.MeasureString(command.Label).X + CommandButtonPadding;
            var button = elementPool.CreateElement<Button>(bar, new ElementOptions
            {
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(x, 0), Size = new Vector2(width, CommandBarHeight), DisplayMode = ElementDisplayMode.Fixed },
                Text = new TextOptions { Text = command.Label },
            });
            button.TooltipText = command.Tooltip;
            button.Enabled = command.IsEnabled;
            button.Clicked += _ => _pendingCommand = command.Run;
            bar.AddChild(button);
            x += width + CommandButtonGap;
        }

        _viewOptionToggles.Clear();
        foreach (var option in ViewOptions)
        {
            var size = new Vector2(Toggle.CheckboxSize + ToggleLabelGap + font.MeasureString(option.Label).X, CommandBarHeight);
            var toggle = elementPool.CreateElement<Toggle>(bar, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = size, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false },
            });
            toggle.Configure(option.Label, LabelPosition.East, option.IsOn(), FieldRow.ToggleOnColor, WindowPalette.EditableControlBackground, WindowPalette.ControlLabelTextColor, font, option.Changed);
            toggle.TooltipText = option.Tooltip;
            bar.AddChild(toggle);
            _viewOptionToggles.Add(toggle);
        }

        LayOutViewOptions(bar);
        var isShown = bar.ChildElements.Count > 0;
        if (bar.IsVisible != isShown)
        {
            bar.IsVisible = isShown;
            LayOutInspector(_area);
        }
    }

    /// <summary>The view switches right-aligned in bar, the last at its right edge.</summary>
    private void LayOutViewOptions(Window bar)
    {
        var right = bar.ContentSize.X;
        for (var index = _viewOptionToggles.Count - 1; index >= 0; index--)
        {
            var toggle = _viewOptionToggles[index];
            var size = toggle.CurrentSize;
            right -= size.X;
            toggle.SetBounds(new Vector2(right, 0), size);
            right -= CommandButtonGap;
        }
    }

    /// <summary>The record's commands at the pointer, after selecting it -- the same ones its buttons run.</summary>
    private void OpenRecordMenu(TRecord record, Point position)
    {
        if (!ReferenceEquals(record, _selectedRecord))
        {
            Select(record);
        }

        var commands = CommandsFor(record);
        if (commands.Count == 0)
        {
            return;
        }

        _context.ContextMenuController.Open(new Vector2(position.X, position.Y), [.. commands.Select(command => new DropdownOption(command.Label, null, command.IsEnabled, () => _pendingCommand = command.Run))]);
    }

    /// <summary>Rebuilds the browser's rows, keeping the selection -- after a record's name or marker changed.</summary>
    protected void RefreshRows()
    {
        if (_isOpen)
        {
            RebuildRows();
        }
    }

    /// <summary>Shows the selected record again in every window -- after it changed.</summary>
    protected void RefreshSelected()
    {
        if (_isOpen && _selectedRecord is { } record)
        {
            Select(record);
        }
    }

    /// <summary>Closes the tab's windows and builds them again over what it shows now, keeping the selection by id -- after the session it reads was rebuilt; nothing while it is closed.</summary>
    protected void Reopen()
    {
        if (!_isOpen)
        {
            return;
        }

        var context = _context;
        var area = _area;
        Close();
        context.RemoveAllInputHandlers();
        Open(context, area);
    }

    public void Open(WorkshopTabContext context, Rectangle area)
    {
        _context = context;
        _isOpen = true;
        _area = area;
        if (_selectedRecord is { } previouslySelected)
        {
            var selectedId = IdOf(previouslySelected);
            _selectedRecord = AllRecords.FirstOrDefault(record => IdOf(record) == selectedId);
        }

        BuildBrowser(area);
        BuildInspector(area);
        BuildPreview(area);
        BuildDetails(area);
        RebuildRows();
        OnOpened(context);

        if (_selectedRecord is { } selectedRecord)
        {
            ShowRelatedRecords(selectedRecord);
            ShowTitles(selectedRecord);
            if (_inspectorWindow is { } inspector)
            {
                ShowInInspector(inspector, selectedRecord);
            }

            RebuildCommandBar(selectedRecord);
            OnSelected(selectedRecord);
        }
        else if (_rows.Count > 0)
        {
            Select(_rows[0].Record);
        }

        ArrangeColumns();
        BringSelectedRowIntoView();
    }

    /// <summary>Scrolls the browser's list so the selected record's row shows, when the search shows it.</summary>
    private void BringSelectedRowIntoView()
    {
        foreach (var (record, row) in _rows)
        {
            if (ReferenceEquals(record, _selectedRecord))
            {
                row.BringIntoView();
                return;
            }
        }
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
        _searchBox.SetMaximumSize(SearchBoxSize());
        _searchBox.SetSize(SearchBoxSize());
        _newRecordButton?.SetRelativePosition(new Vector2(_browserWindow.ContentSize.X - NewRecordButtonWidth, 0));
        _listWindow.SetMaximumSize(ListSize());
        _listWindow.SetSize(ListSize());
        _area = area;
        LayOutInspector(area);
        _previewWindow.SetRelativePosition(PreviewPosition(area));
        _previewWindow.SetMaximumSize(PreviewMaximumSize(area));
        _detailsWindow?.SetRelativePosition(DetailsPosition(area));
        _detailsWindow?.SetMaximumSize(DetailsMaximumSize(area));
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
        _newRecordButton = null;
        _newRecordRequested = false;
        _context.Layers.Remove(UiLayer.Base, _browserWindow);
        _context.Layers.Remove(UiLayer.Base, _previewWindow);
        if (_detailsWindow is { } detailsWindow)
        {
            _context.Layers.Remove(UiLayer.Base, detailsWindow);
            detailsWindow.Close();
            _detailsWindow = null;
        }

        if (_inspectorWindow is { } inspectorWindow)
        {
            _context.Layers.Remove(UiLayer.Base, inspectorWindow);
            inspectorWindow.Close();
            _inspectorWindow = null;
        }

        if (_recordProblemsWindow is { } recordProblemsWindow)
        {
            _context.Layers.Remove(UiLayer.Base, recordProblemsWindow);
            recordProblemsWindow.Close();
            _recordProblemsWindow = null;
        }

        if (_commandBarWindow is { } commandBarWindow)
        {
            _context.Layers.Remove(UiLayer.Base, commandBarWindow);
            commandBarWindow.Close();
            _commandBarWindow = null;
        }

        _pendingCommand = null;

        _browserWindow.Close();
        _previewWindow.Close();
    }

    public void Update(GameTime gameTime)
    {
        if (_searchFilter.Update(_searchBox.OriginalText))
        {
            RebuildRows();
        }

        if (_pendingCommand is { } command)
        {
            _pendingCommand = null;
            command();
        }

        if (_newRecordRequested)
        {
            _newRecordRequested = false;
            if (CreateNewRecord() is { } created)
            {
                RebuildRows();
                Select(created);
                BringSelectedRowIntoView();
            }
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
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = SearchBoxSize(), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.EditableControlBackground },
            Text = new TextOptions { TextColor = WindowPalette.BodyTextColor },
        });
        _searchBox.GhostText = SearchGhostText;
        _searchBox.GhostTextColor = WindowPalette.GhostTextOnLightColor;
        _searchBox.SetText(_searchFilter.AppliedText);
        _browserWindow.AddChild(_searchBox);

        if (NewRecordLabel is { } newRecordLabel)
        {
            _newRecordButton = elementPool.CreateElement<Button>(_browserWindow, new ElementOptions
            {
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(_browserWindow.ContentSize.X - NewRecordButtonWidth, 0), Size = new Vector2(NewRecordButtonWidth, SearchBoxHeight), DisplayMode = ElementDisplayMode.Fixed },
                Text = new TextOptions { Text = newRecordLabel },
            });
            _newRecordButton.TooltipText = NewRecordTooltip;
            _newRecordButton.Clicked += _ => _newRecordRequested = true;
            _browserWindow.AddChild(_newRecordButton);
        }

        _listWindow = elementPool.CreateElement<Window>(_browserWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, SearchBoxHeight + SearchGap), Size = ListSize(), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        _browserWindow.AddChild(_listWindow);
    }

    private void BuildInspector(Rectangle area)
    {
        if (!HasInspector)
        {
            return;
        }

        var elementPool = _context.UiEngine.ElementPoolService;
        _recordProblemsWindow = elementPool.CreateElement<TextWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(MinimumInspectorWidth, RecordProblemsHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false, CanUserFocus = false, CanUserScrollVertical = true },
            Content = new ElementContentOptions { ContentColor = new Color(60, 24, 24) },
            Text = new TextOptions { Text = string.Empty, TextColor = Color.MistyRose, Multiline = true },
        });
        _recordProblemsWindow.Initialize();
        _recordProblemsWindow.IsVisible = false;
        _context.Layers.Add(UiLayer.Base, _recordProblemsWindow);

        _commandBarWindow = elementPool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(MinimumInspectorWidth, CommandBarHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        _commandBarWindow.ContentPadding = Vector2.Zero;
        _commandBarWindow.Initialize();
        _commandBarWindow.IsVisible = false;
        _context.Layers.Add(UiLayer.Base, _commandBarWindow);

        _inspectorWindow = elementPool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(MinimumInspectorWidth, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, TitleText = InspectorTitle, CanUserScrollVertical = true, CanUserFocus = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        _inspectorWindow.Initialize();
        _context.Layers.Add(UiLayer.Base, _inspectorWindow);
        LayOutInspector(area);
    }

    /// <summary>The inspector column between the browser and the preview, under the record's problems while they show.</summary>
    private void LayOutInspector(Rectangle area)
    {
        if (_inspectorWindow is not { } inspector || _recordProblemsWindow is not { } strip)
        {
            return;
        }

        var x = area.X + Margin * 2 + BrowserWidth;
        var width = InspectorWidth(area);
        var top = area.Y + Margin;
        if (strip.IsVisible)
        {
            var stripSize = new Vector2(width, RecordProblemsHeight);
            strip.SetMaximumSize(stripSize);
            strip.SetBounds(new Vector2(x, top), stripSize);
            top += RecordProblemsHeight + Margin;
        }

        if (_commandBarWindow is { IsVisible: true } bar)
        {
            var barSize = new Vector2(width, CommandBarHeight);
            bar.SetMaximumSize(barSize);
            bar.SetBounds(new Vector2(x, top), barSize);
            LayOutViewOptions(bar);
            top += CommandBarHeight + Margin;
        }

        var inspectorSize = new Vector2(width, System.Math.Max(60f, area.Bottom - Margin - top));
        var scroll = inspector.ScrollOffset;
        inspector.SetMaximumSize(inspectorSize);
        inspector.SetBounds(new Vector2(x, top), inspectorSize);
        inspector.ScrollTo(scroll);
    }

    private float InspectorWidth(Rectangle area) => System.Math.Max(MinimumInspectorWidth, area.Width - BrowserWidth - PreviewWidth - Margin * 4);

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
            Text = new TextOptions { Text = appearance.Name, TextColor = appearance.IsDimmed ? DimmedRowTextColor : WindowPalette.BodyTextColor },
        });
        row.AddChild(name);

        row.Clicked += _ => Select(record);
        row.OnRightClicked = position => OpenRecordMenu(record, position);
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

        ShowRelatedRecords(record);
        ShowTitles(record);
        if (_inspectorWindow is { } inspector)
        {
            ShowInInspector(inspector, record);
        }

        RebuildCommandBar(record);
        ArrangeColumns();
        OnSelected(record);
    }

    /// <summary>What names record and what it names, in the bottom panel's Used By and Uses -- after it changed, too.</summary>
    protected void ShowRelatedRecords(TRecord record) => _context.ShowRelatedRecords(UsedByOf(record), UsesOf(record));

    private void ShowTitles(TRecord record)
    {
        _previewWindow.TitleText = PreviewTitleFor(record);
        if (_detailsWindow is { } detailsWindow)
        {
            detailsWindow.TitleText = DetailsTitleFor(record);
        }
    }

    /// <summary>Places the details pane a margin right of the preview's actual right edge: a window that wraps its content can be wider than the width it was built to.</summary>
    private void ArrangeColumns()
    {
        if (_detailsWindow is { } detailsWindow)
        {
            MoveColumnTo(detailsWindow, _previewWindow.RelativePosition.X + _previewWindow.CurrentSize.X + Margin);
        }
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

    /// <summary>The search box's size: the browser's width, less the New button and a gap when the tab has one.</summary>
    private Vector2 SearchBoxSize() => new(_browserWindow.ContentSize.X - (NewRecordLabel is null ? 0f : NewRecordButtonWidth + SearchGap), SearchBoxHeight);

    private Vector2 PreviewPosition(Rectangle area) =>
        HasInspector
            ? new Vector2(area.X + Margin * 3 + BrowserWidth + InspectorWidth(area), area.Y + Margin)
            : new Vector2(area.X + Margin * 2 + BrowserWidth, area.Y + Margin);

    private Vector2 PreviewMaximumSize(Rectangle area) => new(PreviewWidth, area.Height - Margin * 2);

    private Vector2 DetailsPosition(Rectangle area) => PreviewPosition(area) + new Vector2(PreviewWidth + Margin, 0);

    private Vector2 DetailsMaximumSize(Rectangle area) => new(DetailsWidth, area.Height - Margin * 2);
}
