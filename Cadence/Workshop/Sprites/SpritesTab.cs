using Microsoft.Xna.Framework;
using SpriteFormat;
using UIEngine.Bootstrap;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;
using UIEngine.UI.Filtering;

namespace Workshop.Sprites;

/// <summary>The sprite manifest, read-only: every entry in a list, the selected one's sheet with its cells marked, and its placement and joins previewed as the game draws them.</summary>
/// <remarks>
/// <para>
/// Engine-side: it reads the manifest through SpriteFormat and draws through UIEngine, so any game on Cadence gets it. Three
/// top-level windows in the Base layer: the entries (a sortable ListView, a problem glyph on any entry with one) on the left, the
/// sheet (SpriteSheetView, with a choice of sheet when an entry draws from several) in the middle, and the details on the right --
/// a summary, the placement preview (SpritePlacementPreview, with its footprint) and the link preview (SpriteLinkPreview).
/// </para>
/// <para>
/// The entries are narrowed as a browser's records are: a search box by name, the filter button beside it and its chips
/// under it (SpriteEntryFilters: Show only Has problems, Kind any of Options, Linked set, Wall), kept for the user under the
/// tab's title (WorkshopTabContext.BrowserFilters). The title counts what is listed while either narrows it; the selected
/// entry stays in the sheet and details while the list hides it, and its row is selected again once listed.
/// </para>
/// <para>
/// What is wrong with an entry (SpriteEntryView.Problems) goes into the Error List through Problems, which the host adds to its
/// load problems; activating one selects its entry (TrySelectRecord, by SpriteEntryView.Id). Editing is "Workshop: edit sprites and
/// retire SpriteManifestBuilder" (TODO.md); until then entries are authored in Cadence/Tools/SpriteManifestBuilder.
/// </para>
/// </remarks>
public sealed class SpritesTab : IIdeTab
{
    public const string TabTitle = "Sprites";

    private const float Margin = 8f;
    private const float EntriesWidth = 330f;
    private const float DetailsWidth = 380f;

    /// <summary>The narrowest the details column gets before the sheet column gives way to it; the sheet keeps MinimumSheetWidth, and only a tab area too narrow for both overlaps them.</summary>
    private const float MinimumDetailsWidth = 220f;
    private const float MinimumSheetWidth = 100f;
    private const float FieldHeight = 26f;
    private const float FieldGap = 6f;
    private const float FieldLabelWidth = 90f;
    private const float SummaryHeight = 210f;
    private const float PlacementPreviewHeight = 250f;
    private const float LinkPreviewHeight = 170f;
    private const string ManifestSource = "SpriteManifest.json";

    private static readonly string[] FootprintOptions = ["1x1", "2x2", "3x3"];

    private const float SearchBoxHeight = 26f;
    private const float SearchGap = 4f;
    private const float FilterButtonPadding = 14f;
    private const float MaximumChipsHeight = 400f;
    private const string FilterButtonTooltip = "Show only the entries that match: checked choices under one heading combine as the heading says, and every heading must hold";

    private static readonly int SearchDebounceFrames = Engine.Utilities.GameTiming.FramesForSeconds(0.3f);

    private readonly UiEngineContext _uiEngine;
    private readonly IReadOnlyList<SpriteEntryView> _entries;
    private readonly SpriteCellDrawer _drawer;
    private readonly Dictionary<SpriteEntryView, ListViewRow> _rowsByEntry;
    private readonly DebouncedTextFilter _searchFilter = new(SearchDebounceFrames);
    private List<SpriteEntryView> _searchedEntries = [];

    /// <summary>The entries' filter, made the first time the tab opens (with the choices this user last had checked) and kept while the IDE runs.</summary>
    private ListFilter<SpriteEntryView>? _filter;

    private WorkshopTabContext _context = null!;
    private Window _entriesWindow = null!;
    private TextBox _searchBox = null!;
    private ListFilterButton _filterButton = null!;
    private ListFilterChips _filterChips = null!;
    private float _filterButtonWidth;
    private ListView _entriesList = null!;
    private Window _sheetWindow = null!;
    private DropdownField _sheetChoice = null!;
    private SpriteSheetView _sheetView = null!;
    private Window _detailsWindow = null!;
    private TextWindow _summary = null!;
    private DropdownField _footprintChoice = null!;
    private SpritePlacementPreview _placementPreview = null!;
    private SpriteLinkPreview _linkPreview = null!;
    private SpriteEntryView? _selected;
    private bool _isOpen;

    private SpritesTab(UiEngineContext uiEngine, IReadOnlyList<SpriteEntryView> entries, Func<string, bool> sheetExists)
    {
        _uiEngine = uiEngine;
        _entries = entries;
        _drawer = new SpriteCellDrawer(uiEngine.SpriteSheetService, sheetExists);
        _rowsByEntry = entries.ToDictionary<SpriteEntryView, SpriteEntryView, ListViewRow>(entry => entry, RowOf, ReferenceEqualityComparer.Instance);
        Problems =
        [
            .. entries.SelectMany(entry => entry.Problems.Select(problem => new WorkshopProblem(problem.Severity, problem.Description, ManifestSource)
            {
                TabTitle = TabTitle,
                RecordId = entry.Id,
            })),
        ];
    }

    /// <summary>What is wrong with the manifest's entries, for the host to add to the Error List.</summary>
    public IReadOnlyList<WorkshopProblem> Problems { get; }

    public string Title => TabTitle;

    public bool ShowsRelatedRecords => false;

    /// <summary>Reads the manifest at manifestPath and registers the tab's elements with uiEngine's pool -- once per IDE run.</summary>
    /// <param name="spritesheetsDirectory">The spritesheets folder, relative to the exe's output directory, as the host gave UiEngineBootstrapper.</param>
    public static SpritesTab Create(UiEngineContext uiEngine, string manifestPath, string spritesheetsDirectory)
    {
        var pool = uiEngine.ElementPoolService;
        var services = uiEngine.ElementServices;
        pool.RegisterFactory(() => new SpriteSheetView(services));
        pool.RegisterFactory(() => new SpritePlacementPreview(services));
        pool.RegisterFactory(() => new SpriteLinkPreview(services));

        var spritesheetsFolder = Path.Combine(AppContext.BaseDirectory, spritesheetsDirectory);
        Func<string, bool> sheetExists = sheetPath => File.Exists(Path.Combine(spritesheetsFolder, sheetPath));
        return new SpritesTab(uiEngine, SpriteEntryView.Read(SpriteManifest.ReadEntries(manifestPath), sheetExists), sheetExists);
    }

    public void Open(WorkshopTabContext context, Rectangle area)
    {
        _context = context;
        var (entriesBounds, sheetBounds, detailsBounds) = Columns(area);
        var filter = _filter ??= CreateFilter(context);

        _entriesWindow = CreateWindow(entriesBounds, $"Sprites ({_entries.Count})");
        BuildSearchRow(filter);
        _entriesList = CreateChild<ListView>(_entriesWindow, new Vector2(0, ListTop()), ListSize(), showBorder: false);
        _entriesList.SetColumns([new ListViewColumn("Name", 180), new ListViewColumn("Kind", 75), new ListViewColumn("Cells", 45)], showIconColumn: true);
        _entriesList.SelectionChanged += row =>
        {
            if (row?.Tag is SpriteEntryView entry)
            {
                Select(entry);
            }
        };
        _filterChips.Resized += _ => LayOutList();
        _isOpen = true;
        RefreshRows();

        _sheetWindow = CreateWindow(sheetBounds, "Sheet");
        _sheetChoice = CreateChild<DropdownField>(_sheetWindow, Vector2.Zero, new Vector2(_sheetWindow.ContentSize.X, FieldHeight), showBorder: true);
        _sheetChoice.SelectionChanged += index => _sheetView.Show(_selected, _selected?.SheetPaths[index]);
        _sheetView = CreateChild<SpriteSheetView>(_sheetWindow, new Vector2(0, FieldHeight + FieldGap), SheetViewSize(_sheetWindow), showBorder: false);
        _sheetView.Configure(_drawer);

        _detailsWindow = CreateWindow(detailsBounds, "Details");
        _detailsWindow.CanUserScrollVertical = true;
        var width = _detailsWindow.ContentSize.X;
        var y = 0f;
        _summary = CreateText(_detailsWindow, new Vector2(0, y), new Vector2(width, SummaryHeight), string.Empty);
        y += SummaryHeight + FieldGap;
        CreateText(_detailsWindow, new Vector2(0, y), new Vector2(FieldLabelWidth, FieldHeight), "Footprint");
        _footprintChoice = CreateChild<DropdownField>(_detailsWindow, new Vector2(FieldLabelWidth, y), new Vector2(100, FieldHeight), showBorder: true);
        _footprintChoice.SetOptions(FootprintOptions, 0);
        _footprintChoice.SelectionChanged += index => _placementPreview.FootprintTiles = index + 1;
        y += FieldHeight + FieldGap;
        _placementPreview = CreateChild<SpritePlacementPreview>(_detailsWindow, new Vector2(0, y), new Vector2(width, PlacementPreviewHeight), showBorder: true);
        _placementPreview.Configure(_drawer);
        y += PlacementPreviewHeight + FieldGap;
        _linkPreview = CreateChild<SpriteLinkPreview>(_detailsWindow, new Vector2(0, y), new Vector2(width, LinkPreviewHeight), showBorder: true);
        _linkPreview.Configure(_drawer);

        var toSelect = _selected ?? _entriesList.RowsInDisplayOrder.FirstOrDefault()?.Tag as SpriteEntryView ?? _entries.FirstOrDefault();
        SelectListedRow(toSelect);
        Select(toSelect);
    }

    public void Update(GameTime gameTime)
    {
        if (_isOpen && _searchFilter.Update(_searchBox.OriginalText))
        {
            RefreshRows();
        }
    }

    /// <summary>The filter over SpriteEntryFilters.Groups, checking what this user last had checked here; every change is kept and lists the entries again.</summary>
    private ListFilter<SpriteEntryView> CreateFilter(WorkshopTabContext context)
    {
        var filter = new ListFilter<SpriteEntryView>(SpriteEntryFilters.Groups);
        var savedIds = context.BrowserFilters?.CheckedIdsFor(TabTitle) ?? [];
        filter.Selection.SetCheckedIds(savedIds);
        if (filter.Selection.CheckedCount != savedIds.Count)
        {
            context.BrowserFilters?.Save(TabTitle, filter.Selection.CheckedIds());
        }

        filter.Selection.Changed += selection =>
        {
            _context.BrowserFilters?.Save(TabTitle, selection.CheckedIds());
            if (_isOpen)
            {
                RefreshRows();
            }
        };
        var font = _uiEngine.FontService.GetFont(UIEngine.UI.Chrome.FontChrome.DefaultFontSize);
        _filterButtonWidth = font.MeasureString($"{ListFilterButton.DefaultLabel} ({filter.Choices.Count})").X + FilterButtonPadding;
        return filter;
    }

    /// <summary>The search box and the filter button over the entries, and the filter's chips under them.</summary>
    private void BuildSearchRow(ListFilter<SpriteEntryView> filter)
    {
        var elementPool = _uiEngine.ElementPoolService;
        _searchBox = elementPool.CreateElement<TextBox>(_entriesWindow, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = SearchBoxSize(), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.EditableControlBackground },
            Text = new TextOptions { TextColor = WindowPalette.BodyTextColor },
        });
        _searchBox.GhostText = "Search sprites";
        _searchBox.GhostTextColor = WindowPalette.GhostTextOnLightColor;
        _searchBox.SetText(_searchFilter.AppliedText);
        _entriesWindow.AddChild(_searchBox);

        _filterButton = elementPool.CreateElement<ListFilterButton>(_entriesWindow, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = FilterButtonPosition(), Size = new Vector2(_filterButtonWidth, SearchBoxHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
        });
        _filterButton.Configure(new ListFilterMenuSource<SpriteEntryView>(filter, () => _searchedEntries));
        _filterButton.TooltipText = FilterButtonTooltip;
        _entriesWindow.AddChild(_filterButton);

        var chipsWidth = _entriesWindow.ContentSize.X;
        _filterChips = elementPool.CreateElement<ListFilterChips>(_entriesWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, SearchBoxHeight + SearchGap), Size = new Vector2(chipsWidth, 0), MaximumSize = new Vector2(chipsWidth, MaximumChipsHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        _filterChips.ContentPadding = Vector2.Zero;
        _filterChips.Configure(new ListFilterMenuSource<SpriteEntryView>(filter, () => _searchedEntries), chipsWidth);
        _entriesWindow.AddChild(_filterChips);
    }

    /// <summary>
    /// The entries the search and the filter leave, the title counting them while either narrows the list. The selected
    /// entry stays selected and shown in the details while the list hides it, and its row is selected again once listed.
    /// </summary>
    private void RefreshRows()
    {
        var filter = _filter!;
        _searchedEntries = SpriteEntryFilters.Search(_entries, _searchFilter.AppliedText);
        List<SpriteEntryView> listed = [.. _searchedEntries.Where(filter.Matches)];
        _entriesList.SetRows([.. listed.Select(entry => _rowsByEntry[entry])]);
        var isNarrowed = _searchFilter.AppliedText.Length > 0 || !filter.Selection.IsEmpty;
        _entriesWindow.TitleText = isNarrowed ? $"Sprites ({listed.Count} of {_entries.Count})" : $"Sprites ({_entries.Count})";
        SelectListedRow(_selected);
        _filterButton.RefreshMenu();
    }

    /// <summary>Selects entry's row when the list holds it.</summary>
    private void SelectListedRow(SpriteEntryView? entry)
    {
        if (entry is not null && _entriesList.RowsInDisplayOrder.Contains(_rowsByEntry[entry]))
        {
            _entriesList.Select(_rowsByEntry[entry]);
        }
    }

    /// <summary>The entries under the search box, and under the chips while any show, taking the rest of the window's height.</summary>
    private void LayOutList()
    {
        if (!_isOpen)
        {
            return;
        }

        _entriesList.SetMaximumSize(ListSize());
        _entriesList.SetBounds(new Vector2(0, ListTop()), ListSize());
    }

    private float ListTop() => SearchBoxHeight + SearchGap + (_filterChips.IsVisible ? _filterChips.CurrentSize.Y + SearchGap : 0f);

    private Vector2 ListSize() => new(_entriesWindow.ContentSize.X, Math.Max(SearchBoxHeight, _entriesWindow.ContentSize.Y - ListTop()));

    private Vector2 SearchBoxSize() => new(_entriesWindow.ContentSize.X - _filterButtonWidth - SearchGap, SearchBoxHeight);

    private Vector2 FilterButtonPosition() => new(_entriesWindow.ContentSize.X - _filterButtonWidth, 0);

    public void Resize(Rectangle area)
    {
        var (entriesBounds, sheetBounds, detailsBounds) = Columns(area);
        Place(_entriesWindow, entriesBounds);
        _searchBox.SetMaximumSize(SearchBoxSize());
        _searchBox.SetSize(SearchBoxSize());
        _filterButton.SetRelativePosition(FilterButtonPosition());
        _filterChips.SetMaximumSize(new Vector2(_entriesWindow.ContentSize.X, MaximumChipsHeight));
        _filterChips.SetWidth(_entriesWindow.ContentSize.X);
        LayOutList();
        Place(_sheetWindow, sheetBounds);
        _sheetChoice.SetSize(new Vector2(_sheetWindow.ContentSize.X, FieldHeight));
        _sheetView.SetSize(SheetViewSize(_sheetWindow));
        Place(_detailsWindow, detailsBounds);
        var width = _detailsWindow.ContentSize.X;
        _summary.SetSize(new Vector2(width, SummaryHeight));
        _placementPreview.SetSize(new Vector2(width, PlacementPreviewHeight));
        _linkPreview.SetSize(new Vector2(width, LinkPreviewHeight));
    }

    public void Close()
    {
        _isOpen = false;
        foreach (var window in new[] { _entriesWindow, _sheetWindow, _detailsWindow })
        {
            _context.Layers.Remove(UiLayer.Base, window);
            window.Close();
        }
    }

    public bool TrySelectRecord(Guid recordId)
    {
        if (!_isOpen || _entries.FirstOrDefault(entry => entry.Id == recordId) is not { } entry)
        {
            return false;
        }

        SelectListedRow(entry);
        Select(entry);
        return true;
    }

    private void Select(SpriteEntryView? entry)
    {
        _selected = entry;
        _detailsWindow.TitleText = entry is null ? "Details" : entry.Name;
        _summary.UpdateText(entry is null ? string.Empty : SpriteEntrySummary.Of(entry));
        _placementPreview.Show(entry);
        _linkPreview.Show(entry);

        var sheets = entry?.SheetPaths ?? [];
        _sheetChoice.SetOptions([.. sheets], sheets.Count > 0 ? 0 : -1);
        _sheetWindow.TitleText = sheets.Count switch
        {
            0 => "Sheet",
            1 => $"Sheet: {sheets[0]}",
            _ => $"Sheets ({sheets.Count})",
        };
        _sheetView.Show(entry, sheets.Count > 0 ? sheets[0] : null);
    }

    private static ListViewRow RowOf(SpriteEntryView entry)
    {
        var worst = entry.Problems.Count == 0 ? (WorkshopProblemSeverity?)null : entry.Problems.Min(problem => problem.Severity);
        var kind = entry.Kind switch
        {
            SpriteEntryKind.Wall => "Wall",
            SpriteEntryKind.LinkedSet => "Linked",
            _ => "Options",
        };
        var cellCount = entry.Parts.Sum(part => part.Part.Cells.Count(cell => cell is not null));
        return new ListViewRow(
            [entry.Name, kind, cellCount.ToString()],
            worst is { } severity ? WorkshopBottomPanel.GlyphOf(severity) : null,
            worst is { } shownSeverity ? WorkshopBottomPanel.ColorOf(shownSeverity) : null,
            entry);
    }

    private (Rectangle Entries, Rectangle Sheet, Rectangle Details) Columns(Rectangle area)
    {
        var top = (int)(area.Y + Margin);
        var height = (int)Math.Max(100, area.Height - Margin * 2);
        var entriesLeft = (int)(area.X + Margin);
        var sheetLeft = (int)(entriesLeft + EntriesWidth + Margin);
        var detailsWidth = (int)Math.Clamp(area.Right - Margin - (sheetLeft + MinimumSheetWidth + Margin), MinimumDetailsWidth, DetailsWidth);
        var detailsLeft = (int)(area.Right - Margin - detailsWidth);
        return (
            new Rectangle(entriesLeft, top, (int)EntriesWidth, height),
            new Rectangle(sheetLeft, top, (int)Math.Max(MinimumSheetWidth, detailsLeft - Margin - sheetLeft), height),
            new Rectangle(detailsLeft, top, detailsWidth, height));
    }

    private static Vector2 SheetViewSize(Window sheetWindow) =>
        new(sheetWindow.ContentSize.X, Math.Max(50, sheetWindow.ContentSize.Y - FieldHeight - FieldGap));

    private static void Place(Window window, Rectangle bounds)
    {
        window.SetRelativePosition(new Vector2(bounds.X, bounds.Y));
        window.SetSize(new Vector2(bounds.Width, bounds.Height));
    }

    private Window CreateWindow(Rectangle bounds, string title)
    {
        var window = _uiEngine.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(bounds.X, bounds.Y), Size = new Vector2(bounds.Width, bounds.Height), MaximumSize = new Vector2(4000, 4000), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = title, ShowBorder = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
        });
        window.Initialize();
        _context.Layers.Add(UiLayer.Base, window);
        return window;
    }

    private T CreateChild<T>(Window parent, Vector2 position, Vector2 size, bool showBorder)
        where T : Element
    {
        var child = _uiEngine.ElementPoolService.CreateElement<T>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, MaximumSize = new Vector2(4000, 4000), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = showBorder },
        });
        parent.AddChild(child);
        return child;
    }

    private TextWindow CreateText(Window parent, Vector2 position, Vector2 size, string text)
    {
        var textWindow = _uiEngine.ElementPoolService.CreateElement<TextWindow>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Text = new TextOptions { Text = text, TextColor = WindowPalette.BodyTextColor },
        });
        parent.AddChild(textWindow);
        return textWindow;
    }
}
