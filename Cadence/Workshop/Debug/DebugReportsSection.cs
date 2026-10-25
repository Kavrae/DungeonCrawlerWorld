using System.Globalization;
using Engine.Diagnostics;
using Microsoft.Xna.Framework;
using UIEngine.Input;
using UIEngine.UI;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace Workshop.Debug;

/// <summary>The Debug tab's Reports section: every report and saved benchmark run in the report folders, newest first, opened in the same views as Live, and two reports compared.</summary>
/// <remarks>
/// <para>
/// The browser lists what DiagnosticsReportCatalog finds, read off the UI thread when the section first shows and on
/// Refresh. Opening a row (Open, a double-click, Enter or its right-click menu) reads it into a recorded session the Live
/// views show below -- frame costs, memory, a gauge series' graphs, startup scopes; an A/B opens as the comparison it recorded.
/// </para>
/// <para>
/// The tabs under the toolbar -- All, then frame costs, memory, leaks and startup -- pick what the browser lists: All every
/// report, any other only the reports holding it. Compare takes a baseline -- "Use as baseline" -- and another report, and
/// compares what both hold (ReportComparison); the selected tab's comparison shows below, every item largest change first,
/// rows past the threshold coloured, and All shows none. The baseline's row and button show green and the compared report's
/// yellow; a report is never compared with itself. Refresh clears both.
/// </para>
/// </remarks>
public sealed class DebugReportsSection(DebugTabServices services, WorkshopDebugging debugging)
{
    public const string WindowTitle = "Reports";

    private const float ToolbarButtonGap = 4f;
    private const float ThresholdLabelWidth = 90f;
    private const float ThresholdFieldWidth = 70f;
    private const float TabTileWidth = 100f;
    private const float MinimumBrowserHeight = 200f;
    private const float MaximumBrowserHeight = 320f;
    private const string CompareHint = "Use a report as the baseline, select another, then Compare with baseline.";

    public static readonly Color BaselineRowColor = Color.LightGreen * 0.8f;
    public static readonly Color ComparedRowColor = Color.Yellow * 0.55f;
    public static readonly Color BaselineButtonColor = new(40, 120, 40);
    public static readonly Color ComparedButtonColor = Color.Gold;

    private static readonly Color RegressionColor = Color.IndianRed * 0.45f;
    private static readonly Color ImprovementColor = Color.LightGreen * 0.6f;

    private readonly List<(LiveView? View, TextWindow Tile)> _tabTiles = [];
    private Task<IReadOnlyList<DiagnosticsReportEntry>>? _scan;
    private IReadOnlyList<DiagnosticsReportEntry> _entries = [];
    private Window _browser = null!;
    private Button _open = null!;
    private Button _useAsBaseline = null!;
    private Button _compare = null!;
    private NumericField _threshold = null!;
    private TextWindow _baselineLabel = null!;
    private TextWindow _previousLabel = null!;
    private ListView _list = null!;
    private Window _compareWindow = null!;
    private TextWindow _compareSummary = null!;
    private TextWindow _compareMessage = null!;
    private ListView _compareList = null!;
    private DiagnosticsReportEntry? _baselineEntry;
    private LiveDiagnosticsSession? _baselineSession;
    private DiagnosticsReportEntry? _comparedEntry;
    private (string PreviousName, LiveDiagnosticsSession Previous, string CurrentName, LiveDiagnosticsSession Current)? _comparedSessions;
    private ReportComparison? _comparison;
    private LiveView? _selectedTab;
    private LiveDiagnosticsSession? _openSession;
    private bool _showsComparison;
    private bool _isVisible;
    private bool _hasScanned;

    /// <summary>The opened report's views: the Live section's, over a recorded session.</summary>
    public DebugLiveSection Viewer { get; } = new(services, "Report", "No report open: select one above, then Open it.");

    public ListView ReportList => _list;

    public ListView ComparisonList => _compareList;

    public string ComparisonSummary => _compareSummary.OriginalText;

    /// <summary>"Previous: " and the file compared against, beside the tabs; empty with no comparison or under All.</summary>
    public string ComparisonPrevious => _previousLabel.OriginalText;

    public string ComparisonMessage => _compareMessage.OriginalText;

    public Button OpenButton => _open;

    public Button UseAsBaselineButton => _useAsBaseline;

    public Button CompareButton => _compare;

    public NumericField Threshold => _threshold;

    /// <summary>Every report the last scan found, newest first, whatever the tab.</summary>
    public IReadOnlyList<DiagnosticsReportEntry> Entries => _entries;

    /// <summary>The reports the selected tab lists, newest first.</summary>
    public IEnumerable<DiagnosticsReportEntry> ListedEntries => _entries.Where(Lists);

    /// <summary>True while the report folders are being read.</summary>
    public bool IsScanning => _scan is { IsCompleted: false };

    /// <summary>The session the opened report reads into; null when none is open or it opened as a comparison.</summary>
    public LiveDiagnosticsSession? OpenSession => _openSession;

    /// <summary>True when the comparison, not the opened report, shows under the browser: one was made and a tab other than All is selected.</summary>
    public bool ShowsComparison => _showsComparison && _selectedTab is not null;

    public DiagnosticsReportEntry? Baseline => _baselineEntry;

    public DiagnosticsReportEntry? Compared => _comparedEntry;

    /// <summary>The comparison shown; null before one.</summary>
    public ReportComparison? Comparison => _comparison;

    /// <summary>The tab selected: what the browser lists and the comparison shows; null for All.</summary>
    public LiveView? SelectedTab => _selectedTab;

    /// <summary>view's tile under the toolbar; null for All.</summary>
    public TextWindow TabTileFor(LiveView? view) => _tabTiles.First(entry => entry.View == view).Tile;

    public void Open(Rectangle bounds)
    {
        var (browserBounds, viewerBounds) = Rows(bounds);
        _browser = DebugTabElements.CreateWindow(services, browserBounds, WindowTitle);
        var x = 0f;
        CreateButton("Refresh", ref x, StartScan);
        _open = CreateButton("Open", ref x, () => WithSelected(OpenEntry));
        _useAsBaseline = CreateButton("Use as baseline", ref x, () => WithSelected(UseAsBaseline));
        _compare = CreateButton("Compare with baseline", ref x, () => WithSelected(CompareWithBaseline));
        DebugTabElements.CreateText(services, _browser, new Vector2(x, 0), new Vector2(ThresholdLabelWidth, DebugTabChrome.RowHeight), "Threshold %");
        x += ThresholdLabelWidth;
        _threshold = DebugTabElements.CreateChild<NumericField>(services, _browser, new Vector2(x, 0), new Vector2(ThresholdFieldWidth, DebugTabChrome.RowHeight), showBorder: true);
        _threshold.Configure(BenchmarkComparison.DefaultThresholdPercent, 1, 500);
        _threshold.ValueChanged += _ => RefreshComparison();
        x += ThresholdFieldWidth + DebugTabChrome.Margin;
        _baselineLabel = DebugTabElements.CreateText(services, _browser, new Vector2(x, 0), new Vector2(System.Math.Max(1, _browser.ContentSize.X - x), DebugTabChrome.RowHeight), string.Empty);
        _tabTiles.Clear();
        var tileX = 0f;
        foreach (var view in TabViews)
        {
            _tabTiles.Add((view, DebugTabElements.CreateTile(services, _browser, new Vector2(tileX, TabsTop), TabTileWidth, view is { } shown ? LabelOf(shown) : "All", () => ShowTab(view))));
            tileX += TabTileWidth;
        }

        var previousX = System.Math.Max(x, tileX + DebugTabChrome.Margin);
        _previousLabel = DebugTabElements.CreateText(services, _browser, new Vector2(previousX, TabsTop), new Vector2(System.Math.Max(1, _browser.ContentSize.X - previousX), DebugTabChrome.RowHeight), string.Empty);
        _list = DebugTabElements.CreateChild<ListView>(services, _browser, new Vector2(0, ListTop), ListSize(), showBorder: true);
        _list.SetColumns([new("Kind", 100), new("Date", 140), new("Seed", 80), new("Frames", 100), new("Detail", 380), new("File", 260)], showIconColumn: false);
        _list.RowActivated += _ => WithSelected(OpenEntry);
        services.AddInputHandler(new ReportRowMenuInputHandler(this));

        Viewer.Open(viewerBounds);
        _compareWindow = DebugTabElements.CreateWindow(services, viewerBounds, "Compare");
        _compareSummary = DebugTabElements.CreateText(services, _compareWindow, Vector2.Zero, new Vector2(_compareWindow.ContentSize.X, DebugTabChrome.RowHeight), CompareHint);
        _compareMessage = DebugTabElements.CreateText(services, _compareWindow, new Vector2(0, CompareMessageTop), new Vector2(_compareWindow.ContentSize.X, DebugTabChrome.RowHeight), string.Empty);
        _compareList = DebugTabElements.CreateChild<ListView>(services, _compareWindow, new Vector2(0, CompareListTop), CompareListSize(), showBorder: true);

        RefreshComparison();
        ShowTab(_selectedTab);
    }

    public void Resize(Rectangle bounds)
    {
        var (browserBounds, viewerBounds) = Rows(bounds);
        DebugTabElements.Place(_browser, browserBounds);
        _list.SetSize(ListSize());
        _baselineLabel.SetSize(new Vector2(System.Math.Max(1, _browser.ContentSize.X - _baselineLabel.RelativePosition.X), DebugTabChrome.RowHeight));
        _previousLabel.SetSize(new Vector2(System.Math.Max(1, _browser.ContentSize.X - _previousLabel.RelativePosition.X), DebugTabChrome.RowHeight));
        Viewer.Resize(viewerBounds);
        DebugTabElements.Place(_compareWindow, viewerBounds);
        _compareSummary.SetSize(new Vector2(_compareWindow.ContentSize.X, DebugTabChrome.RowHeight));
        _compareMessage.SetSize(new Vector2(_compareWindow.ContentSize.X, DebugTabChrome.RowHeight));
        _compareList.SetSize(CompareListSize());
    }

    /// <summary>Takes up a finished scan and shows the opened report. UI thread, once per frame, while the section shows.</summary>
    public void Update()
    {
        if (_scan is { IsCompleted: true } scan)
        {
            _scan = null;
            _entries = scan.Status == TaskStatus.RanToCompletion ? scan.Result : [];
            ShowEntries();
            ShowMarks();
            if (scan.Exception is { } exception)
            {
                services.Status.Show($"Couldn't read the report folders: {exception.GetBaseException().Message}");
            }
        }

        if (!ShowsComparison)
        {
            Viewer.Update(_openSession);
        }
    }

    public void SetVisible(bool isVisible)
    {
        _isVisible = isVisible;
        if (isVisible && !_hasScanned)
        {
            StartScan();
        }

        _browser.IsVisible = isVisible;
        Viewer.Window.IsVisible = isVisible && !ShowsComparison;
        _compareWindow.IsVisible = isVisible && ShowsComparison;
    }

    public void Close()
    {
        DebugTabElements.Close(services, _browser);
        DebugTabElements.Close(services, Viewer.Window);
        DebugTabElements.Close(services, _compareWindow);
    }

    /// <summary>Clears the baseline, the compared report, the comparison and the selected row, then reads the report folders again, off the UI thread.</summary>
    public void StartScan()
    {
        if (IsScanning)
        {
            return;
        }

        _hasScanned = true;
        ClearSelections();
        var directories = debugging.ReportDirectories;
        _scan = Task.Run(() => DiagnosticsReportCatalog.Scan(directories));
        _browser.TitleText = $"{WindowTitle} (reading...)";
    }

    /// <summary>Blocks until a scan in progress has finished, and takes it up -- for a caller that needs the list now.</summary>
    public void WaitForScan()
    {
        _scan?.Wait();
        Update();
    }

    /// <summary>What a right-click on entry's row offers: Open, Use as baseline and Compare with baseline, each enabled only where it can be done.</summary>
    public IReadOnlyList<DropdownOption> MenuOptionsFor(DiagnosticsReportEntry entry) =>
    [
        new("Open", null, Enabled: true, () => OpenEntry(entry)),
        new("Use as baseline", null, Enabled: entry.CanBeCompared && !IsBaseline(entry), () => UseAsBaseline(entry)),
        new("Compare with baseline", null, Enabled: entry.CanBeCompared && _baselineEntry is not null && !IsBaseline(entry), () => CompareWithBaseline(entry)),
    ];

    /// <summary>Opens entry: into the views below, or as the comparison an A/B recorded.</summary>
    public void OpenEntry(DiagnosticsReportEntry entry)
    {
        try
        {
            if (entry.Kind == DiagnosticsReportKind.AbComparison)
            {
                var (previous, current) = DiagnosticsReportFiles.OpenAbComparison(entry);
                ShowComparison(entry, $"{entry.FileName} (A, baseline build)", previous, $"{entry.FileName} (B, current build)", current);
                return;
            }

            _openSession = DiagnosticsReportFiles.OpenSession(entry);
            Viewer.Window.TitleText = $"Report: {entry.FileName}";
            _showsComparison = false;
            SetVisible(_isVisible);
            services.Status.Show($"Opened {entry.FileName}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            services.Status.Show($"Couldn't open {entry.FileName}: {exception.Message}");
        }
    }

    /// <summary>Makes entry the baseline later reports are compared with.</summary>
    public void UseAsBaseline(DiagnosticsReportEntry entry)
    {
        if (!entry.CanBeCompared)
        {
            services.Status.Show($"{entry.FileName} is an A/B, already a comparison: open it instead.");
            return;
        }

        if (TryOpen(entry) is not { } session)
        {
            return;
        }

        _baselineEntry = entry;
        _baselineSession = session;
        if (_comparedEntry is { } compared && SameFile(compared, entry))
        {
            _comparedEntry = null;
        }

        ShowMarks();
        services.Status.Show($"Baseline: {entry.FileName}.");
    }

    /// <summary>Compares entry with the baseline; refused with no baseline, for the baseline itself, and for an A/B.</summary>
    public void CompareWithBaseline(DiagnosticsReportEntry entry)
    {
        if (_baselineEntry is not { } baselineEntry || _baselineSession is not { } baselineSession)
        {
            services.Status.Show("Choose a baseline first: select a report, then Use as baseline.");
            return;
        }

        if (IsBaseline(entry))
        {
            services.Status.Show($"{entry.FileName} is the baseline: choose another report to compare with it.");
            return;
        }

        if (!entry.CanBeCompared)
        {
            services.Status.Show($"{entry.FileName} is an A/B, already a comparison: open it instead.");
            return;
        }

        if (TryOpen(entry) is { } session)
        {
            ShowComparison(entry, baselineEntry.FileName, baselineSession, entry.FileName, session);
        }
    }

    /// <summary>Selects view's tab: the browser lists only the reports holding it, and the comparison shows it; null (All) lists every report and shows no comparison.</summary>
    public void ShowTab(LiveView? view)
    {
        _selectedTab = view;
        foreach (var (tileView, tile) in _tabTiles)
        {
            DebugTabElements.StyleTile(tile, isSelected: tileView == view);
        }

        ShowEntries();
        ShowMarks();
        ShowComparisonRows();
        SetVisible(_isVisible);
    }

    private static IReadOnlyList<LiveView?> TabViews => [null, .. ReportComparison.Views.Select(static view => (LiveView?)view)];

    private bool Lists(DiagnosticsReportEntry entry) => _selectedTab is not { } tab || entry.Holds(tab);

    private void ClearSelections()
    {
        _baselineEntry = null;
        _baselineSession = null;
        _comparedEntry = null;
        _comparedSessions = null;
        _comparison = null;
        _showsComparison = false;
        _list.Select(null, scrollIntoView: false);
        _compareWindow.TitleText = "Compare";
        _compareSummary.UpdateText(CompareHint);
        ShowMarks();
        ShowComparisonRows();
        SetVisible(_isVisible);
    }

    private bool IsBaseline(DiagnosticsReportEntry entry) => _baselineEntry is { } baseline && SameFile(baseline, entry);

    private static bool SameFile(DiagnosticsReportEntry first, DiagnosticsReportEntry second) =>
        string.Equals(first.FilePath, second.FilePath, StringComparison.OrdinalIgnoreCase);

    private DiagnosticsReportEntry? SelectedEntry => _list.SelectedRow?.Tag as DiagnosticsReportEntry;

    private void WithSelected(Action<DiagnosticsReportEntry> action)
    {
        if (SelectedEntry is { } entry)
        {
            action(entry);
            return;
        }

        services.Status.Show("Select a report first.");
    }

    private LiveDiagnosticsSession? TryOpen(DiagnosticsReportEntry entry)
    {
        try
        {
            return DiagnosticsReportFiles.OpenSession(entry);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            services.Status.Show($"Couldn't read {entry.FileName}: {exception.Message}");
            return null;
        }
    }

    /// <summary>Compares current with previous, keeping the selected tab if it has rows, else selecting the first that does.</summary>
    private void ShowComparison(DiagnosticsReportEntry comparedEntry, string previousName, LiveDiagnosticsSession previous, string currentName, LiveDiagnosticsSession current)
    {
        _comparedEntry = comparedEntry;
        _comparedSessions = (previousName, previous, currentName, current);
        RefreshComparison();
        var comparison = _comparison!;
        var view = _selectedTab is { } selected && comparison.TabFor(selected).Rows.Count > 0
            ? selected
            : ReportComparison.Views.FirstOrDefault(candidate => comparison.TabFor(candidate).Rows.Count > 0, _selectedTab ?? LiveView.FrameCosts);
        _compareWindow.TitleText = $"Compare: {currentName} with {previousName}";
        _showsComparison = true;
        ShowTab(view);
    }

    private void RefreshComparison()
    {
        if (_comparedSessions is not { } sessions)
        {
            return;
        }

        _comparison = ReportComparison.Compare(sessions.PreviousName, sessions.Previous, sessions.CurrentName, sessions.Current, _threshold.Value);
        _compareSummary.UpdateText(SummaryOf(_comparison, _threshold.Value));
        ShowComparisonRows();
    }

    private static string SummaryOf(ReportComparison comparison, double thresholdPercent)
    {
        var counts = comparison.Tabs
            .Where(static tab => tab.Unavailable is null)
            .Select(static tab => $"{LabelOf(tab.View)}: {tab.Rows.Count(static row => row.Flag == BenchmarkComparisonFlag.Regression)} up, {tab.Rows.Count(static row => row.Flag == BenchmarkComparisonFlag.Improvement)} down");
        return $"Past {thresholdPercent:0.#}%   {string.Join("   ", counts)}";
    }

    /// <summary>The selected tab's rows, coloured past the threshold, under a line saying what they are or why there are none; and the previous file's name beside the tabs.</summary>
    private void ShowComparisonRows()
    {
        if (_comparison is not { } comparison || _comparedSessions is not { } sessions || _selectedTab is not { } view)
        {
            _previousLabel.UpdateText(string.Empty);
            _compareMessage.UpdateText(string.Empty);
            _compareList.SetRows([]);
            return;
        }

        _previousLabel.UpdateText($"Previous: {sessions.PreviousName}");
        var tab = comparison.TabFor(view);
        _compareList.SetColumns([new("Item", 420), new($"Previous ({tab.Unit})", 130), new($"Current ({tab.Unit})", 130), new("Change", 100), new("Change %", 80), new("Ratio", 70), new("Flag", 100)], showIconColumn: false);
        var rows = tab.Rows.Select(row => new ListViewRow(
        [
            row.Name,
            Format(tab.Unit, row.Previous),
            Format(tab.Unit, row.Current),
            (row.Change >= 0 ? "+" : string.Empty) + Format(tab.Unit, row.Change),
            row.ChangePercent?.ToString("0.0", CultureInfo.InvariantCulture) ?? "n/a",
            row.Ratio?.ToString("0.000", CultureInfo.InvariantCulture) ?? "n/a",
            row.Flag == BenchmarkComparisonFlag.None ? string.Empty : row.Flag.ToString(),
        ],
        Tag: row)).ToList();
        _compareList.SetRows(rows);
        _compareList.SetRowColors(rows
            .Where(static row => ((BenchmarkComparisonRow)row.Tag!).Flag != BenchmarkComparisonFlag.None)
            .Select(static row => KeyValuePair.Create(row, ((BenchmarkComparisonRow)row.Tag!).Flag == BenchmarkComparisonFlag.Regression ? RegressionColor : ImprovementColor)));
        _compareMessage.UpdateText(tab.Unavailable ?? (rows.Count == 0
            ? $"Nothing to compare: neither holds any {LabelOf(view).ToLowerInvariant()}."
            : $"{LabelOf(view)} in {tab.Unit}, largest change first."));
    }

    private static string Format(string unit, double value) => value.ToString(unit switch
    {
        "ms/frame" => "0.0000",
        "MB" => "0.000",
        "% growth" => "0",
        _ => "0.0",
    }, CultureInfo.InvariantCulture);

    /// <summary>Marks the baseline's row and button green and the compared report's yellow.</summary>
    private void ShowMarks()
    {
        var colors = new List<KeyValuePair<ListViewRow, Color>>();
        foreach (var row in _list.RowsInDisplayOrder)
        {
            var entry = (DiagnosticsReportEntry)row.Tag!;
            if (IsBaseline(entry))
            {
                colors.Add(KeyValuePair.Create(row, BaselineRowColor));
            }
            else if (_comparedEntry is { } compared && SameFile(compared, entry))
            {
                colors.Add(KeyValuePair.Create(row, ComparedRowColor));
            }
        }

        _list.SetRowColors(colors);
        StyleButton(_useAsBaseline, _baselineEntry is null ? null : BaselineButtonColor, Color.White);
        StyleButton(_compare, _comparedEntry is null ? null : ComparedButtonColor, Color.Black);
        _baselineLabel.UpdateText(_baselineEntry is { } baseline ? $"Baseline: {baseline.FileName}" : "No baseline chosen.");
    }

    private static void StyleButton(Button button, Color? color, Color textColor)
    {
        button.SetContentColor(color ?? WindowPalette.ControlBackground);
        button.TextColor = color is null ? WindowPalette.ControlLabelTextColor : textColor;
    }

    /// <summary>Lists the reports the selected tab holds, keeping the selected report selected while it is still listed.</summary>
    private void ShowEntries()
    {
        var selectedPath = SelectedEntry?.FilePath;
        var listed = ListedEntries.ToList();
        _browser.TitleText = IsScanning
            ? $"{WindowTitle} (reading...)"
            : _selectedTab is null ? $"{WindowTitle} ({_entries.Count:N0})" : $"{WindowTitle} ({listed.Count:N0} of {_entries.Count:N0})";
        _list.SetRows([.. listed.Select(static entry => new ListViewRow(
        [
            KindLabel(entry.Kind),
            entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            entry.Seed?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            entry.StartFrame is { } start && entry.EndFrame is { } end ? $"{start}-{end}" : string.Empty,
            entry.Detail,
            entry.FileName,
        ],
        Tag: entry))]);
        var reselected = selectedPath is null ? null : _list.RowsInDisplayOrder.FirstOrDefault(row => string.Equals(((DiagnosticsReportEntry)row.Tag!).FilePath, selectedPath, StringComparison.OrdinalIgnoreCase));
        _list.Select(reselected, scrollIntoView: false);
    }

    private static string KindLabel(DiagnosticsReportKind kind) => kind switch
    {
        DiagnosticsReportKind.Periodic => "Latest report",
        DiagnosticsReportKind.SavedRun => "Saved run",
        DiagnosticsReportKind.AbComparison => "A/B",
        _ => kind.ToString(),
    };

    private static string LabelOf(LiveView view) => view switch
    {
        LiveView.FrameCosts => "Frame costs",
        _ => view.ToString(),
    };

    private Button CreateButton(string label, ref float x, Action onClicked)
    {
        var width = services.Fonts.GetFont(FontChrome.DefaultFontSize).MeasureString(label).X + 20;
        var button = services.Pool.CreateElement<Button>(_browser, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(x, 0), Size = new Vector2(width, DebugTabChrome.RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.ControlBackground },
            Text = new TextOptions { Text = label, TextColor = WindowPalette.ControlLabelTextColor },
        });
        button.Clicked += _ => onClicked();
        _browser.AddChild(button);
        x += width + ToolbarButtonGap;
        return button;
    }

    private static float TabsTop => DebugTabChrome.RowHeight + DebugTabChrome.RowGap;

    private static float ListTop => TabsTop + DebugTabChrome.RowHeight + DebugTabChrome.RowGap;

    private static float CompareMessageTop => DebugTabChrome.RowHeight + DebugTabChrome.RowGap;

    private static float CompareListTop => CompareMessageTop + DebugTabChrome.RowHeight;

    private Vector2 ListSize() =>
        new(_browser.ContentSize.X, System.Math.Max(DebugTabChrome.RowHeight, _browser.ContentSize.Y - ListTop));

    private Vector2 CompareListSize() =>
        new(_compareWindow.ContentSize.X, System.Math.Max(DebugTabChrome.RowHeight, _compareWindow.ContentSize.Y - CompareListTop));

    private static (Rectangle Browser, Rectangle Viewer) Rows(Rectangle bounds)
    {
        var browserHeight = (int)System.Math.Clamp(bounds.Height * 0.38f, MinimumBrowserHeight, MaximumBrowserHeight);
        var viewerTop = bounds.Y + browserHeight + (int)DebugTabChrome.RowGap;
        return (
            new Rectangle(bounds.X, bounds.Y, bounds.Width, browserHeight),
            new Rectangle(bounds.X, viewerTop, bounds.Width, System.Math.Max(100, bounds.Bottom - viewerTop)));
    }

    /// <summary>Selects the report row under position and opens its menu there; nothing off the rows or while the section is hidden.</summary>
    private void OpenRowMenuAt(Point position)
    {
        if (!_browser.IsVisible || _list.RowAt(position) is not { Tag: DiagnosticsReportEntry entry } row)
        {
            return;
        }

        _list.Select(row, scrollIntoView: false);
        services.ContextMenus.Open(new Vector2(position.X, position.Y), MenuOptionsFor(entry));
    }

    /// <summary>A right-click on a report's row selects it and opens its menu.</summary>
    private sealed class ReportRowMenuInputHandler(DebugReportsSection section) : IUiInputHandler
    {
        public void OnRightPress(Point position) => section.OpenRowMenuAt(position);
    }
}
