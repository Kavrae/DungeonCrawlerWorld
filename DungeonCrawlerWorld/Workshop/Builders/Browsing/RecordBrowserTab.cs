using Engine.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;
using UIEngine.UI.Fields;
using UIEngine.UI.Filtering;
using Workshop;

namespace Builders.Browsing;

/// <summary>How a record's row reads in the browser: its icon, its name and the marker symbols after it, greyed when the record is out of play (disabled).</summary>
public readonly record struct RecordRowAppearance(string Name, string? SpriteName, string Glyph, Color GlyphColor)
{
    public bool IsDimmed { get; init; }

    /// <summary>The symbols after the name, in reading order, each with its own tooltip; none by default.</summary>
    public IReadOnlyList<RecordRowMarker> Markers { get; init; } = [];
}

/// <summary>Something a tab does to one record (Publish, Delete): a button over the inspector and an option on the record's right-click menu, both running Run; Tooltip says what it does, or why it can't.</summary>
public sealed record RecordCommand(string Label, string Tooltip, bool IsEnabled, Action Run)
{
    /// <summary>The key that runs it while the browser has focus, shown beside its menu option; null for none. Pressed while it is disabled, the status line says why (Tooltip).</summary>
    public Keys? Key { get; init; }
}

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
/// <para>
/// A tab offering filters (FilterGroups) has a filter button at the search box's right end. The list is what the search
/// left (BuildSections) less what the filters remove, a heading left empty dropped; while either narrows it the title
/// reads "Items (4 of 13)", and a list narrowed to nothing says why, with Clear filters. The checked choices outlive the
/// tab's windows: reopened (a session swapped) it keeps every one its groups still offer, and they are kept for the user
/// between runs under the tab's title (WorkshopTabContext.BrowserFilters), a choice no longer offered (a mod removed)
/// dropped as the tab first opens. They show as chips between the
/// search box and the list, which takes whatever height the chips leave.
/// </para>
/// <para>
/// A selected record the list doesn't hold -- a filter checked or the search typed while it is selected, an edit that
/// stopped it matching, New or navigation to one the list hides -- stays selected and is pinned above the list, its last
/// symbol a pin; once the list holds it again, or another record is selected, the pinned row goes.
/// </para>
/// </remarks>
public abstract class RecordBrowserTab<TRecord, TPreview> : IIdeTab
    where TRecord : class
    where TPreview : Window
{
    private const float Margin = 8f;
    private const float BrowserWidth = 340f;
    private const float SearchBoxHeight = 26f;
    private const float SearchGap = 4f;
    private const float RowHeight = RecordRow.Height;

    /// <summary>The width every preview row is built to by default -- Item Details' own width beside the player's Inventory window.</summary>
    protected const float DefaultPreviewWidth = 460f;

    /// <summary>The width the preview is built to; a tab whose preview needs more room (a map) widens it.</summary>
    protected virtual float PreviewWidth => DefaultPreviewWidth;

    private const float DefaultDetailsWidth = 360f;

    private static readonly int SearchDebounceFrames = GameTiming.FramesForSeconds(0.3f);
    private static readonly Color RowColor = WindowPalette.PanelContentColor;
    private static readonly Color SelectedRowColor = WindowPalette.ListSelectionColor;
    private static readonly Color HeadingColor = WindowPalette.ControlBackground;

    private readonly DebouncedTextFilter _searchFilter = new(SearchDebounceFrames);
    private readonly List<(TRecord Record, Window Row)> _rows = [];

    private const float MinimumInspectorWidth = 300f;
    private const float RecordProblemsHeight = 52f;
    private const string InspectorTitle = "Inspector";

    private const float NewRecordButtonWidth = 56f;

    private const string FilterButtonTooltip = "Show only the records that match: checked choices under one heading combine as the heading says, and every heading must hold";
    private const float EmptyListMessageHeight = 44f;
    private const float ClearFiltersButtonWidth = 96f;

    private readonly List<TRecord> _searchedRecords = [];
    private ListFilter<TRecord>? _filter;
    private ListFilterButton? _filterButton;
    private ListFilterChips? _filterChips;
    private float _filterButtonWidth;

    /// <summary>The records the list holds now, the pinned one not counted.</summary>
    private readonly HashSet<TRecord> _listedRecords = new(ReferenceEqualityComparer.Instance);

    /// <summary>The selected record shown above the list because the list doesn't hold it; null while it does.</summary>
    private TRecord? _pinnedRecord;

    /// <summary>The rows to be built again on the next Update -- a selection that pins or unpins a row, asked from inside a row's own click.</summary>
    private bool _rowsRebuildRequested;

    /// <summary>The last symbol on a pinned row: the selected record, which the list doesn't hold.</summary>
    private static readonly RecordRowMarker PinnedMarker = new("📌", null, WindowPalette.BodyTextColor, "Selected; not in the filtered list");

    /// <summary>The tallest the chips may grow, wrapped, before the list under them.</summary>
    private const float MaximumChipsHeight = 400f;
    private bool _clearFiltersRequested;
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

    /// <summary>The filters the browser offers beside its search box, in menu order; none, the default, for no filter button.</summary>
    protected virtual IReadOnlyList<ListFilterGroup<TRecord>> FilterGroups => [];

    /// <summary>The tab's records in the plural, lower case, as a list narrowed to nothing names them ("No items match ...").</summary>
    protected virtual string RecordsNoun => Title.ToLowerInvariant();

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
            });
            button.SetText(command.Label);
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
            toggle.Configure(option.IsOn(), FieldRow.ToggleOnColor, WindowPalette.EditableControlBackground, option.Changed);
            toggle.SetLabel(option.Label, font, WindowPalette.ControlLabelTextColor);
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

        _context.ContextMenuController.Open(new Vector2(position.X, position.Y), [.. commands.Select(command => new DropdownOption(command.Label, KeyLabelOf(command.Key), command.IsEnabled, () => _pendingCommand = command.Run))]);
    }

    private static string? KeyLabelOf(Keys? key) => key switch
    {
        null => null,
        Keys.Delete => "Del",
        Keys.Insert => "Ins",
        { } other => other.ToString(),
    };

    /// <summary>
    /// A key pressed while the browser (its list, or anything in it but the search box) has focus: the list's moves
    /// (ListKeyNavigation) over the listed records, a page the rows the list shows at once; a selected record's command
    /// by its key (RecordCommand.Key), as its button runs it, or why it can't on the status line; Insert, New.
    /// </summary>
    private bool OnBrowserKeyPressed(Keys key)
    {
        if (key == Keys.Insert)
        {
            if (NewRecordLabel is null)
            {
                return false;
            }

            _newRecordRequested = true;
            return true;
        }

        return TryRunCommandKey(key) || TryMoveSelection(key);
    }

    private bool TryRunCommandKey(Keys key)
    {
        if (_selectedRecord is not { } record)
        {
            return false;
        }

        foreach (var command in CommandsFor(record))
        {
            if (command.Key != key)
            {
                continue;
            }

            if (command.IsEnabled)
            {
                _pendingCommand = command.Run;
            }
            else
            {
                _context.Status.Show(command.Tooltip);
            }

            return true;
        }

        return false;
    }

    /// <summary>Moves the selection over the records the list holds, in the order shown -- never onto a heading, the pinned row or the empty-list message -- bringing the newly selected row into view.</summary>
    private bool TryMoveSelection(Keys key)
    {
        var firstListedRow = _pinnedRecord is null ? 0 : 1;
        var listedCount = _rows.Count - firstListedRow;
        var selectedIndex = -1;
        for (var index = firstListedRow; index < _rows.Count; index++)
        {
            if (ReferenceEquals(_rows[index].Record, _selectedRecord))
            {
                selectedIndex = index - firstListedRow;
                break;
            }
        }

        var rowsPerPage = (int)(_listWindow.ContentSize.Y / RowHeight);
        if (!ListKeyNavigation.TryMove(key, selectedIndex, listedCount, rowsPerPage, out var target))
        {
            return false;
        }

        if (target >= 0 && target != selectedIndex)
        {
            Select(_rows[firstListedRow + target].Record);
            BringSelectedRowIntoView();
        }

        return true;
    }

    /// <summary>A key pressed while the inspector or a control in it has focus: Home focuses its first tab stop, End its last, in the order Tab walks, brought into view.</summary>
    private bool OnInspectorKeyPressed(Keys key)
    {
        if (key is not (Keys.Home or Keys.End) || _inspectorWindow is not { } inspector)
        {
            return false;
        }

        var tabStops = new List<Element>();
        inspector.CollectTabStops(tabStops);
        if (tabStops.Count == 0)
        {
            return true;
        }

        var target = key == Keys.Home ? tabStops[0] : tabStops[^1];
        _context.FocusElement(target);
        target.BringIntoView();
        return true;
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

        BuildFilter();
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
        _newRecordButton?.SetRelativePosition(NewRecordButtonPosition());
        _filterButton?.SetRelativePosition(FilterButtonPosition());
        _filterChips?.SetWidth(ControlRowsWidth);
        _listWindow.SetMaximumSize(ListSize());
        _listWindow.SetBounds(new Vector2(0, ListTop()), ListSize());
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
        _searchedRecords.Clear();
        _newRecordButton = null;
        _newRecordRequested = false;
        _filterButton = null;
        _filterChips = null;
        _clearFiltersRequested = false;
        _rowsRebuildRequested = false;
        _listedRecords.Clear();
        _pinnedRecord = null;
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

        if (_clearFiltersRequested)
        {
            _clearFiltersRequested = false;
            _filter?.Selection.Clear();
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

        if (_rowsRebuildRequested)
        {
            _rowsRebuildRequested = false;
            RebuildRows();
            BringSelectedRowIntoView();
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
        _browserWindow.OnKeyPressed = OnBrowserKeyPressed;
        _context.Layers.Add(UiLayer.Base, _browserWindow);

        _searchBox = elementPool.CreateElement<TextBox>(_browserWindow, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(ControlRowsInset, 0), Size = SearchBoxSize(), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.EditableControlBackground },
            Text = new TextOptions { TextColor = WindowPalette.BodyTextColor },
        });
        _searchBox.GhostText = SearchGhostText;
        _searchBox.GhostTextColor = WindowPalette.GhostTextOnLightColor;
        _searchBox.SetText(_searchFilter.AppliedText);
        _browserWindow.AddChild(_searchBox);

        if (_filter is { } filter)
        {
            _filterButton = elementPool.CreateElement<ListFilterButton>(_browserWindow, new ElementOptions
            {
                Layout = new ElementLayoutOptions { RelativePosition = FilterButtonPosition(), Size = new Vector2(_filterButtonWidth, SearchBoxHeight), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
            });
            _filterButton.Configure(new ListFilterMenuSource<TRecord>(filter, () => _searchedRecords));
            _filterButton.TooltipText = FilterButtonTooltip;
            _browserWindow.AddChild(_filterButton);

            var chipsWidth = ControlRowsWidth;
            _filterChips = elementPool.CreateElement<ListFilterChips>(_browserWindow, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(ControlRowsInset, SearchBoxHeight + SearchGap), Size = new Vector2(chipsWidth, 0), MaximumSize = new Vector2(chipsWidth, MaximumChipsHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            });
            _filterChips.ContentPadding = Vector2.Zero;
            _filterChips.Configure(new ListFilterMenuSource<TRecord>(filter, () => _searchedRecords), chipsWidth);
            _browserWindow.AddChild(_filterChips);
        }

        if (NewRecordLabel is { } newRecordLabel)
        {
            _newRecordButton = elementPool.CreateElement<Button>(_browserWindow, new ElementOptions
            {
                Layout = new ElementLayoutOptions { RelativePosition = NewRecordButtonPosition(), Size = new Vector2(NewRecordButtonWidth, SearchBoxHeight), DisplayMode = ElementDisplayMode.Fixed },
            });
            _newRecordButton.SetText(newRecordLabel);
            _newRecordButton.TooltipText = NewRecordTooltip;
            _newRecordButton.Clicked += _ => _newRecordRequested = true;
            _browserWindow.AddChild(_newRecordButton);
        }

        _listWindow = elementPool.CreateElement<Window>(_browserWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, ListTop()), Size = ListSize(), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true, CanUserFocus = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        _browserWindow.AddChild(_listWindow);
        if (_filterChips is { } chips)
        {
            chips.Resized += _ => LayOutList();
        }
    }

    /// <summary>The list under the search box, and under the chips while any show, taking the rest of the browser's height.</summary>
    /// <remarks>Followed only once the list exists: chips checked before the tab opened (saved, or kept through a session swap) lay themselves out as they are added, and the list is first placed under them.</remarks>
    private void LayOutList()
    {
        if (!_isOpen)
        {
            return;
        }

        var position = new Vector2(0, ListTop());
        if (_listWindow.RelativePosition == position && _listWindow.CurrentSize == ListSize())
        {
            return;
        }

        _listWindow.SetMaximumSize(ListSize());
        _listWindow.SetBounds(position, ListSize());
    }

    /// <summary>Where the list starts: under the search box, and under the chips and a gap while any show.</summary>
    private float ListTop() =>
        SearchBoxHeight + SearchGap + (_filterChips is { IsVisible: true } chips ? chips.CurrentSize.Y + SearchGap : 0f);

    /// <summary>
    /// The filter over FilterGroups as the tab offers them now, keeping every choice still offered that was checked --
    /// on the first open, the ones this user last had checked here (WorkshopTabContext.BrowserFilters); none for a tab
    /// offering no group.
    /// </summary>
    private void BuildFilter()
    {
        var checkedIds = _filter?.Selection.CheckedIds() ?? _context.BrowserFilters?.CheckedIdsFor(Title) ?? [];
        if (_filter is { } previous)
        {
            previous.Selection.Changed -= OnFilterChanged;
        }

        var groups = FilterGroups;
        _filter = groups.Count == 0 ? null : new ListFilter<TRecord>(groups);
        if (_filter is not { } filter)
        {
            return;
        }

        filter.Selection.SetCheckedIds(checkedIds);
        if (filter.Selection.CheckedCount != checkedIds.Count)
        {
            _context.BrowserFilters?.Save(Title, filter.Selection.CheckedIds());
        }

        filter.Selection.Changed += OnFilterChanged;
        var font = _context.UiEngine.FontService.GetFont(UIEngine.UI.Chrome.FontChrome.DefaultFontSize);
        _filterButtonWidth = ListFilterButton.WidthFor(font, filter.Choices.Count);
    }

    private void OnFilterChanged(ListFilterSelection selection)
    {
        _context.BrowserFilters?.Save(Title, selection.CheckedIds());
        if (_isOpen)
        {
            RebuildRows();
            BringSelectedRowIntoView();
        }
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
        _inspectorWindow.OnKeyPressed = OnInspectorKeyPressed;
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
        var listedSections = ListedSections(BuildSections(_searchFilter.AppliedText));
        ShowBrowserTitle();
        _pinnedRecord = _selectedRecord is { } selected && !_listedRecords.Contains(selected) ? selected : null;
        if (_pinnedRecord is { } pinned)
        {
            var pinnedRow = CreateRecordRow(rowWidth, pinned, isPinned: true);
            _listWindow.AddChild(pinnedRow);
            _rows.Add((pinned, pinnedRow));
        }

        if (_rowsListed == 0 && IsListNarrowed)
        {
            AddEmptyListRows(rowWidth);
        }

        foreach (var section in listedSections)
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
                var row = CreateRecordRow(rowWidth, record, isPinned: false);
                _listWindow.AddChild(row);
                _rows.Add((record, row));
            }
        }

        _filterButton?.RefreshMenu();
    }

    /// <summary>How many records the list holds now.</summary>
    private int _rowsListed;

    /// <summary>Whether the search or a filter narrows the list.</summary>
    private bool IsListNarrowed => _searchFilter.AppliedText.Length > 0 || _filter is { Selection.IsEmpty: false };

    /// <summary>
    /// The search's sections less what the filters remove, a heading left empty dropped; keeps every record the search
    /// left (what the filter menu counts over) and how many are listed.
    /// </summary>
    private IReadOnlyList<RecordBrowserSection<TRecord>> ListedSections(IReadOnlyList<RecordBrowserSection<TRecord>> searchedSections)
    {
        _searchedRecords.Clear();
        _listedRecords.Clear();
        var searched = new HashSet<TRecord>(ReferenceEqualityComparer.Instance);
        var listed = _listedRecords;
        var listedSections = new List<RecordBrowserSection<TRecord>>(searchedSections.Count);
        foreach (var section in searchedSections)
        {
            var records = new List<TRecord>(section.Records.Count);
            foreach (var record in section.Records)
            {
                if (searched.Add(record))
                {
                    _searchedRecords.Add(record);
                }

                if (_filter is null || _filter.Matches(record))
                {
                    records.Add(record);
                    listed.Add(record);
                }
            }

            if (records.Count > 0)
            {
                listedSections.Add(records.Count == section.Records.Count ? section : section with { Records = records });
            }
        }

        _rowsListed = listed.Count;
        return listedSections;
    }

    /// <summary>The browser's own title while nothing narrows the list; "Items (4 of 13)" while anything does.</summary>
    private void ShowBrowserTitle() =>
        _browserWindow.TitleText = IsListNarrowed ? $"{Title} ({_rowsListed} of {AllRecords.Count()})" : BrowserTitle;

    /// <summary>A list narrowed to nothing: what narrowed it -- the search text, quoted, then each checked filter -- and Clear filters while any is checked.</summary>
    private void AddEmptyListRows(float rowWidth)
    {
        var elementPool = _context.UiEngine.ElementPoolService;
        var reasons = new List<string>();
        if (_searchFilter.AppliedText.Length > 0)
        {
            reasons.Add($"\"{_searchFilter.AppliedText}\"");
        }

        if (_filter is { } filter)
        {
            for (var choiceIndex = 0; choiceIndex < filter.Choices.Count; choiceIndex++)
            {
                if (filter.Selection.IsChecked(choiceIndex))
                {
                    reasons.Add(filter.Choices[choiceIndex].Label);
                }
            }
        }

        var messageSize = new Vector2(rowWidth, EmptyListMessageHeight);
        var message = elementPool.CreateElement<TextWindow>(_listWindow, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = messageSize, MinimumSize = messageSize, MaximumSize = messageSize, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Text = new TextOptions { Text = $"No {RecordsNoun} match {string.Join(", ", reasons)}", TextColor = WindowPalette.TitleTextColor, Multiline = true },
        });
        _listWindow.AddChild(message);

        if (_filter is not { Selection.IsEmpty: false })
        {
            return;
        }

        var buttonSize = new Vector2(ClearFiltersButtonWidth, RowHeight);
        var clear = elementPool.CreateElement<Button>(_listWindow, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = buttonSize, MinimumSize = buttonSize, MaximumSize = buttonSize, DisplayMode = ElementDisplayMode.Fixed },
        });
        clear.SetText(ListFilterButton.ClearFiltersLabel);
        clear.Clicked += _ => _clearFiltersRequested = true;
        _listWindow.AddChild(clear);
    }

    /// <summary>The record's row (RecordRow); a click on any of it selects the record. A pinned row (the selected record, which the list doesn't hold) says so with a last symbol.</summary>
    private Window CreateRecordRow(float width, TRecord record, bool isPinned)
    {
        var appearance = AppearanceOf(record);
        if (isPinned)
        {
            appearance = appearance with { Markers = [.. appearance.Markers, PinnedMarker] };
        }

        var rowColor = ReferenceEquals(record, _selectedRecord) ? SelectedRowColor : RowColor;
        var row = RecordRow.Create(_context.UiEngine.ElementPoolService, _listWindow, width, appearance, rowColor);

        row.Clicked += _ =>
        {
            _context.FocusElement(_listWindow);
            Select(record);
        };
        row.OnRightClicked = position =>
        {
            _context.FocusElement(_listWindow);
            OpenRecordMenu(record, position);
        };
        return row;
    }

    private void Select(TRecord record)
    {
        _selectedRecord = record;
        if (_isOpen && !ReferenceEquals(record, _pinnedRecord) && (_pinnedRecord is not null || !_listedRecords.Contains(record)))
        {
            _rowsRebuildRequested = true;
        }

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

    private Vector2 ListSize() => new(_browserWindow.ContentSize.X, System.Math.Max(RowHeight, _browserWindow.ContentSize.Y - ListTop()));

    /// <summary>How far the rows over the list (search, filter and New; the chips) sit in from the browser's sides -- the list's own padding, so they line up with its rows.</summary>
    private static float ControlRowsInset => UIEngine.UI.Chrome.WindowChrome.Padding;

    /// <summary>The width the rows over the list share: the browser's, less ControlRowsInset on each side.</summary>
    private float ControlRowsWidth => _browserWindow.ContentSize.X - ControlRowsInset * 2;

    /// <summary>The New button's place: the right end of the control rows.</summary>
    private Vector2 NewRecordButtonPosition() => new(ControlRowsInset + ControlRowsWidth - NewRecordButtonWidth, 0);

    /// <summary>The search box's size: the control rows' width, less the filter button (against it) and the New button (a gap before it), when the tab has them.</summary>
    private Vector2 SearchBoxSize() => new(ControlRowsWidth - NewRecordButtonSpace - FilterButtonSpace, SearchBoxHeight);

    /// <summary>The filter button's place: at the search box's right end, left of the New button.</summary>
    private Vector2 FilterButtonPosition() => new(ControlRowsInset + ControlRowsWidth - NewRecordButtonSpace - _filterButtonWidth, 0);

    private float NewRecordButtonSpace => NewRecordLabel is null ? 0f : NewRecordButtonWidth + SearchGap;

    /// <summary>The filter button's share of the search row: its width, with no gap, so it sits against the search box.</summary>
    private float FilterButtonSpace => _filter is null ? 0f : _filterButtonWidth;

    private Vector2 PreviewPosition(Rectangle area) =>
        HasInspector
            ? new Vector2(area.X + Margin * 3 + BrowserWidth + InspectorWidth(area), area.Y + Margin)
            : new Vector2(area.X + Margin * 2 + BrowserWidth, area.Y + Margin);

    private Vector2 PreviewMaximumSize(Rectangle area) => new(PreviewWidth, area.Height - Margin * 2);

    private Vector2 DetailsPosition(Rectangle area) => PreviewPosition(area) + new Vector2(PreviewWidth + Margin, 0);

    private Vector2 DetailsMaximumSize(Rectangle area) => new(DetailsWidth, area.Height - Margin * 2);
}
