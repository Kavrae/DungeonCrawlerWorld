using Microsoft.Xna.Framework;
using SpriteFormat;
using UIEngine.Bootstrap;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

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

    private readonly UiEngineContext _uiEngine;
    private readonly IReadOnlyList<SpriteEntryView> _entries;
    private readonly SpriteCellDrawer _drawer;

    private WorkshopTabContext _context = null!;
    private Window _entriesWindow = null!;
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

    public bool ShowsDependents => false;

    /// <summary>Reads the manifest at manifestPath and registers the tab's elements with uiEngine's pool -- once per IDE run.</summary>
    /// <param name="spritesheetsDirectory">The spritesheets folder, relative to the exe's output directory, as the host gave UiEngineBootstrapper.</param>
    public static SpritesTab Create(UiEngineContext uiEngine, string manifestPath, string spritesheetsDirectory)
    {
        var pool = uiEngine.ElementPoolService;
        pool.RegisterFactory(() => new SpriteSheetView(uiEngine.FontService, pool, uiEngine.RenderService));
        pool.RegisterFactory(() => new SpritePlacementPreview(uiEngine.FontService, pool, uiEngine.RenderService));
        pool.RegisterFactory(() => new SpriteLinkPreview(uiEngine.FontService, pool, uiEngine.RenderService));

        var spritesheetsFolder = Path.Combine(AppContext.BaseDirectory, spritesheetsDirectory);
        Func<string, bool> sheetExists = sheetPath => File.Exists(Path.Combine(spritesheetsFolder, sheetPath));
        return new SpritesTab(uiEngine, SpriteEntryView.Read(SpriteManifest.ReadEntries(manifestPath), sheetExists), sheetExists);
    }

    public void Open(WorkshopTabContext context, Rectangle area)
    {
        _context = context;
        var (entriesBounds, sheetBounds, detailsBounds) = Columns(area);

        _entriesWindow = CreateWindow(entriesBounds, $"Sprites ({_entries.Count})");
        _entriesList = CreateChild<ListView>(_entriesWindow, Vector2.Zero, _entriesWindow.ContentSize, showBorder: false);
        _entriesList.SetColumns([new ListViewColumn("Name", 180), new ListViewColumn("Kind", 75), new ListViewColumn("Cells", 45)], showIconColumn: true);
        _entriesList.SetRows([.. _entries.Select(RowOf)]);
        _entriesList.SelectionChanged += row => Select(row?.Tag as SpriteEntryView);

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

        _isOpen = true;
        var toSelect = _selected ?? _entries.FirstOrDefault();
        _entriesList.Select(_entriesList.RowsInDisplayOrder.FirstOrDefault(row => ReferenceEquals(row.Tag, toSelect)));
        Select(toSelect);
    }

    public void Resize(Rectangle area)
    {
        var (entriesBounds, sheetBounds, detailsBounds) = Columns(area);
        Place(_entriesWindow, entriesBounds);
        _entriesList.SetSize(_entriesWindow.ContentSize);
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

        _entriesList.Select(_entriesList.RowsInDisplayOrder.FirstOrDefault(row => ReferenceEquals(row.Tag, entry)));
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
