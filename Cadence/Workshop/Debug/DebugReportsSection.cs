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
/// A row of tabs runs across the top -- All, then gauges, frame costs, memory, leaks and startup -- over two columns: on the
/// left the toolbar and the browser, on the right the opened report or the comparison. The tabs are the page's one choice of
/// what to look at: they pick what the browser lists (All every report, any other only the reports holding it) and what the
/// right column shows of the opened report or the comparison (All every view it has, one after another).
/// </para>
/// <para>
/// The browser lists what DiagnosticsReportCatalog finds, read off the UI thread when the section first shows and on
/// Refresh. Opening a row (Open, a double-click, Enter or its right-click menu) reads it into a recorded session the Live
/// views show -- frame costs, memory, a gauge series' graphs, startup scopes; an A/B opens as the comparison it recorded.
/// </para>
/// <para>
/// Compare takes a baseline -- "Use as baseline" -- and another report, and compares what both hold (ReportComparison),
/// every item largest change first, rows past the threshold coloured; gauges are series, never compared. The baseline's row
/// and button show green and the compared report's yellow; a report is never compared with itself. Refresh clears both.
/// </para>
/// <para>
/// A benchmark a connected game completes (WorkshopDebugging.CompletedBenchmark) is a Refresh too, after which its report
/// is selected and opened -- the next time the section shows, if it isn't showing; nothing switches to the section by itself.
/// </para>
/// </remarks>
public sealed class DebugReportsSection(DebugTabServices services, WorkshopDebugging debugging)
{
    public const string WindowTitle = "Reports";

    /// <summary>What the comparison says under the Gauges tab.</summary>
    public const string GaugesNotComparedText = "Gauges aren't compared: each report's gauges are a series over time. Open a report to see its graph.";

    private const float ToolbarButtonGap = 4f;
    private const float ThresholdLabelWidth = 90f;
    private const float ThresholdFieldWidth = 70f;
    private const float TabTileWidth = 100f;
    private const float BrowserWidth = 520f;
    private const float KindColumnWidth = 100f;
    private const float DateColumnWidth = 140f;
    private const float MinimumDetailColumnWidth = 120f;
    private const float ListScrollbarAllowance = 24f;
    private const string CompareHint = "Use a report as the baseline, select another, then Compare with baseline.";

    public static readonly Color BaselineRowColor = Color.LightGreen * 0.8f;
    public static readonly Color ComparedRowColor = Color.Yellow * 0.55f;
    public static readonly Color BaselineButtonColor = new(40, 120, 40);
    public static readonly Color ComparedButtonColor = Color.Gold;

    private static readonly Color RegressionColor = Color.IndianRed * 0.45f;
    private static readonly Color ImprovementColor = Color.LightGreen * 0.6f;

    private readonly List<(LiveView? View, TextWindow Tile)> _tabTiles = [];
    private readonly Dictionary<LiveView, TextWindow> _compareMessages = [];
    private readonly Dictionary<LiveView, ListView> _compareLists = [];
    private Task<IReadOnlyList<DiagnosticsReportEntry>>? _scan;
    private IReadOnlyList<DiagnosticsReportEntry> _entries = [];
    private Window _tabBar = null!;
    private Window _browser = null!;
    private Button _open = null!;
    private Button _useAsBaseline = null!;
    private Button _compare = null!;
    private NumericField _threshold = null!;
    private TextWindow _baselineLabel = null!;
    private ListView _list = null!;
    private Window _compareWindow = null!;
    private TextWindow _compareSummary = null!;
    private TextWindow _compareNote = null!;
    private DiagnosticsReportEntry? _openEntry;
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
    private int _shownCompletedBenchmarkVersion;
    private string? _completedReportToOpen;
    private bool _rescansWhenDone;

    /// <summary>The opened report's views: the Live section's, over a recorded session, with no switch of their own -- the page's tabs choose.</summary>
    public DebugLiveSection Viewer { get; } = new(services, "Report", "No report open: select one, then Open it.", hasViewSwitch: false);

    public ListView ReportList => _list;

    /// <summary>The comparison's rows for view, whether or not they are shown.</summary>
    public ListView ComparisonListFor(LiveView view) => _compareLists[view];

    /// <summary>The line over view's comparison, as shown; empty when it isn't shown.</summary>
    public string ComparisonMessageFor(LiveView view) => _compareMessages[view].IsVisible ? _compareMessages[view].OriginalText : string.Empty;

    public string ComparisonSummary => _compareSummary.OriginalText;

    /// <summary>What the comparison says in place of rows -- under the Gauges tab; empty otherwise.</summary>
    public string ComparisonNote => _compareNote.IsVisible ? _compareNote.OriginalText : string.Empty;

    /// <summary>The comparison's window, titled with the two files compared.</summary>
    public Window ComparisonWindow => _compareWindow;

    public Window Browser => _browser;

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

    /// <summary>True when the comparison, not the opened report, shows in the right column.</summary>
    public bool ShowsComparison => _showsComparison;

    public DiagnosticsReportEntry? Baseline => _baselineEntry;

    public DiagnosticsReportEntry? Compared => _comparedEntry;

    /// <summary>The comparison shown; null before one.</summary>
    public ReportComparison? Comparison => _comparison;

    /// <summary>The tab selected: what the browser lists and the right column shows; null for All.</summary>
    public LiveView? SelectedTab => _selectedTab;

    /// <summary>view's tile in the tab row; null for All.</summary>
    public TextWindow TabTileFor(LiveView? view) => _tabTiles.First(entry => entry.View == view).Tile;

    public void Open(Rectangle bounds)
    {
        var (tabBarBounds, browserBounds, detailsBounds) = Areas(bounds);
        _tabBar = CreateTabBar(tabBarBounds);
        _tabTiles.Clear();
        var tileX = 0f;
        foreach (var view in TabViews)
        {
            _tabTiles.Add((view, DebugTabElements.CreateTile(services, _tabBar, new Vector2(tileX, 0), TabTileWidth, view is { } shown ? LabelOf(shown) : "All", () => ShowTab(view))));
            tileX += TabTileWidth;
        }

        _browser = DebugTabElements.CreateWindow(services, browserBounds, WindowTitle);
        var x = 0f;
        CreateButton("Refresh", ref x, StartScan);
        _open = CreateButton("Open", ref x, () => WithSelected(OpenEntry));
        _useAsBaseline = CreateButton("Use as baseline", ref x, () => WithSelected(UseAsBaseline));
        _compare = CreateButton("Compare with baseline", ref x, () => WithSelected(CompareWithBaseline));
        DebugTabElements.CreateText(services, _browser, new Vector2(0, SecondToolbarRowTop), new Vector2(ThresholdLabelWidth, DebugTabChrome.RowHeight), "Threshold %");
        _threshold = DebugTabElements.CreateChild<NumericField>(services, _browser, new Vector2(ThresholdLabelWidth, SecondToolbarRowTop), new Vector2(ThresholdFieldWidth, DebugTabChrome.RowHeight), showBorder: true);
        _threshold.Configure(BenchmarkComparison.DefaultThresholdPercent, 1, 500);
        _threshold.ValueChanged += _ => RefreshComparison();
        _baselineLabel = DebugTabElements.CreateText(services, _browser, new Vector2(BaselineLabelLeft, SecondToolbarRowTop), new Vector2(BaselineLabelWidth(), DebugTabChrome.RowHeight), string.Empty);
        _list = DebugTabElements.CreateChild<ListView>(services, _browser, new Vector2(0, ListTop), ListSize(), showBorder: true);
        _list.SetColumns(BrowserColumns(), showIconColumn: false);
        _list.RowActivated += _ => WithSelected(OpenEntry);
        services.AddInputHandler(new ReportRowMenuInputHandler(this));

        Viewer.Open(detailsBounds);
        _compareWindow = DebugTabElements.CreateWindow(services, detailsBounds, "Compare");
        _compareSummary = DebugTabElements.CreateText(services, _compareWindow, Vector2.Zero, new Vector2(_compareWindow.ContentSize.X, DebugTabChrome.RowHeight), CompareHint);
        _compareNote = DebugTabElements.CreateText(services, _compareWindow, Vector2.Zero, new Vector2(_compareWindow.ContentSize.X, DebugTabChrome.RowHeight), GaugesNotComparedText);
        _compareMessages.Clear();
        _compareLists.Clear();
        foreach (var view in ReportComparison.Views)
        {
            _compareMessages[view] = DebugTabElements.CreateText(services, _compareWindow, Vector2.Zero, new Vector2(_compareWindow.ContentSize.X, DebugTabChrome.RowHeight), string.Empty);
            _compareLists[view] = DebugTabElements.CreateChild<ListView>(services, _compareWindow, Vector2.Zero, Vector2.One, showBorder: true);
        }

        RefreshComparison();
        ShowTab(_selectedTab);
    }

    public void Resize(Rectangle bounds)
    {
        var (tabBarBounds, browserBounds, detailsBounds) = Areas(bounds);
        DebugTabElements.Place(_tabBar, tabBarBounds);
        DebugTabElements.Place(_browser, browserBounds);
        _list.SetSize(ListSize());
        _baselineLabel.SetSize(new Vector2(BaselineLabelWidth(), DebugTabChrome.RowHeight));
        Viewer.Resize(detailsBounds);
        DebugTabElements.Place(_compareWindow, detailsBounds);
        _compareSummary.SetSize(new Vector2(_compareWindow.ContentSize.X, DebugTabChrome.RowHeight));
        LayOutComparison();
    }

    /// <summary>Takes up a finished scan and shows the opened report. UI thread, once per frame, while the section shows.</summary>
    public void Update()
    {
        TakeUpCompletedBenchmark();
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

            if (_rescansWhenDone)
            {
                _rescansWhenDone = false;
                StartScan();
            }
            else if (_completedReportToOpen is { } fileName)
            {
                _completedReportToOpen = null;
                OpenCompletedBenchmark(fileName);
            }
        }

        if (!_showsComparison)
        {
            Viewer.Update(_openSession);
        }
    }

    public void SetVisible(bool isVisible)
    {
        _isVisible = isVisible;
        if (isVisible)
        {
            TakeUpCompletedBenchmark();
            if (!_hasScanned)
            {
                StartScan();
            }
        }

        _tabBar.IsVisible = isVisible;
        _browser.IsVisible = isVisible;
        Viewer.Window.IsVisible = isVisible && !_showsComparison;
        _compareWindow.IsVisible = isVisible && _showsComparison;
    }

    public void Close()
    {
        DebugTabElements.Close(services, _tabBar);
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

    /// <summary>Opens entry: into the right column's views, or as the comparison an A/B recorded.</summary>
    public void OpenEntry(DiagnosticsReportEntry entry) => OpenEntry(entry, announces: true);

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

    /// <summary>Selects view's tab: the browser lists only the reports holding it, and the right column shows only it; null (All) lists every report and shows every view, one after another.</summary>
    public void ShowTab(LiveView? view)
    {
        _selectedTab = view;
        foreach (var (tileView, tile) in _tabTiles)
        {
            DebugTabElements.StyleTile(tile, isSelected: tileView == view);
        }

        ShowEntries();
        ShowMarks();
        ShowDetails();
    }

    private static IReadOnlyList<LiveView?> TabViews => [null, .. Enum.GetValues<LiveView>().Select(static view => (LiveView?)view)];

    private bool Lists(DiagnosticsReportEntry entry) => _selectedTab is not { } tab || entry.Holds(tab);

    /// <param name="announces">Say on the status line that it opened; false when the status line already says why.</param>
    private void OpenEntry(DiagnosticsReportEntry entry, bool announces)
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
            _openEntry = entry;
            Viewer.Window.TitleText = $"Report: {entry.FileName}";
            _showsComparison = false;
            ShowDetails();
            if (announces)
            {
                services.Status.Show($"Opened {entry.FileName}.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            services.Status.Show($"Couldn't open {entry.FileName}: {exception.Message}");
        }
    }

    /// <summary>The right column for the selected tab: the comparison, or the opened report -- every view it holds under All, else the tab's view.</summary>
    private void ShowDetails()
    {
        SetVisible(_isVisible);
        if (_showsComparison)
        {
            LayOutComparison();
            return;
        }

        if (_selectedTab is { } view)
        {
            Viewer.ShowView(view);
            return;
        }

        Viewer.ShowViews(_openEntry is { } entry ? ViewsHeldBy(entry) : []);
    }

    /// <summary>The views entry holds, in the tabs' order; every one when it names none, so each says what it lacks.</summary>
    private static IReadOnlyList<LiveView> ViewsHeldBy(DiagnosticsReportEntry entry)
    {
        var held = Enum.GetValues<LiveView>().Where(entry.Holds).ToList();
        return held.Count > 0 ? held : Enum.GetValues<LiveView>();
    }

    /// <summary>A benchmark a game completed since the section last looked: read the folders again, then open its report.</summary>
    private void TakeUpCompletedBenchmark()
    {
        if (debugging.CompletedBenchmarkVersion == _shownCompletedBenchmarkVersion || debugging.CompletedBenchmark is not { } completion)
        {
            return;
        }

        _shownCompletedBenchmarkVersion = debugging.CompletedBenchmarkVersion;
        _completedReportToOpen = Path.GetFileName(completion.BenchmarkReportPath);
        if (IsScanning)
        {
            _rescansWhenDone = true;
            return;
        }

        StartScan();
    }

    /// <summary>Selects and opens the report named fileName, listing every report if the selected tab hides it; says so when the folders don't hold it.</summary>
    private void OpenCompletedBenchmark(string fileName)
    {
        if (_entries.FirstOrDefault(entry => string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase)) is not { } entry)
        {
            services.Status.Show($"The completed benchmark's report, {fileName}, isn't in the report folders.");
            return;
        }

        if (!Lists(entry))
        {
            ShowTab(null);
        }

        if (_list.RowsInDisplayOrder.FirstOrDefault(row => ReferenceEquals(row.Tag, entry)) is { } row)
        {
            _list.Select(row, scrollIntoView: true);
        }

        OpenEntry(entry, announces: false);
    }

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
        FillComparisonLists();
        ShowMarks();
        ShowDetails();
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

    /// <summary>Compares current with previous and shows it in the right column, under whichever tab is selected.</summary>
    private void ShowComparison(DiagnosticsReportEntry comparedEntry, string previousName, LiveDiagnosticsSession previous, string currentName, LiveDiagnosticsSession current)
    {
        _comparedEntry = comparedEntry;
        _comparedSessions = (previousName, previous, currentName, current);
        _compareWindow.TitleText = $"Compare: {currentName} with {previousName}";
        _showsComparison = true;
        RefreshComparison();
        ShowMarks();
        ShowDetails();
    }

    private void RefreshComparison()
    {
        if (_comparedSessions is not { } sessions)
        {
            return;
        }

        _comparison = ReportComparison.Compare(sessions.PreviousName, sessions.Previous, sessions.CurrentName, sessions.Current, _threshold.Value);
        _compareSummary.UpdateText(SummaryOf(_comparison, _threshold.Value));
        FillComparisonLists();
        LayOutComparison();
    }

    private static string SummaryOf(ReportComparison comparison, double thresholdPercent)
    {
        var counts = comparison.Tabs
            .Where(static tab => tab.Unavailable is null)
            .Select(static tab => $"{LabelOf(tab.View)}: {tab.Rows.Count(static row => row.Flag == BenchmarkComparisonFlag.Regression)} up, {tab.Rows.Count(static row => row.Flag == BenchmarkComparisonFlag.Improvement)} down");
        return $"Past {thresholdPercent:0.#}%   {string.Join("   ", counts)}";
    }

    /// <summary>Every compared view's rows, coloured past the threshold, under a line saying what they are or why there are none.</summary>
    private void FillComparisonLists()
    {
        foreach (var view in ReportComparison.Views)
        {
            var list = _compareLists[view];
            if (_comparison is not { } comparison)
            {
                list.SetRows([]);
                _compareMessages[view].UpdateText(string.Empty);
                continue;
            }

            var tab = comparison.TabFor(view);
            list.SetColumns([new("Item", 420), new($"Previous ({tab.Unit})", 130), new($"Current ({tab.Unit})", 130), new("Change", 100), new("Change %", 80), new("Ratio", 70), new("Flag", 100)], showIconColumn: false);
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
            list.SetRows(rows);
            list.SetRowColors(rows
                .Where(static row => ((BenchmarkComparisonRow)row.Tag!).Flag != BenchmarkComparisonFlag.None)
                .Select(static row => KeyValuePair.Create(row, ((BenchmarkComparisonRow)row.Tag!).Flag == BenchmarkComparisonFlag.Regression ? RegressionColor : ImprovementColor)));
            _compareMessages[view].UpdateText(tab.Unavailable ?? (rows.Count == 0
                ? $"Nothing to compare: neither holds any {LabelOf(view).ToLowerInvariant()}."
                : $"{LabelOf(view)} in {tab.Unit}, largest change first."));
        }
    }

    /// <summary>Places the comparison for the selected tab under its summary: every compared view in turn, as tall as its rows, under All; the tab's view filling the window under another; a note under Gauges.</summary>
    private void LayOutComparison()
    {
        var width = _compareWindow.ContentSize.X;
        var height = _compareWindow.ContentSize.Y;
        var y = DebugTabChrome.RowHeight + DebugTabChrome.RowGap;
        var hasComparison = _comparison is not null;
        _compareWindow.CanUserScrollVertical = _selectedTab is null;
        _compareWindow.ScrollTo(Vector2.Zero);
        _compareNote.IsVisible = hasComparison && _selectedTab == LiveView.Gauges;
        DebugTabElements.Place(_compareNote, new Rectangle(0, (int)y, (int)width, (int)DebugTabChrome.RowHeight));
        foreach (var view in ReportComparison.Views)
        {
            var message = _compareMessages[view];
            var list = _compareLists[view];
            var isShown = hasComparison && (_selectedTab is null || _selectedTab == view);
            message.IsVisible = isShown;
            list.IsVisible = isShown && list.RowsInDisplayOrder.Any();
            if (!isShown)
            {
                continue;
            }

            DebugTabElements.Place(message, new Rectangle(0, (int)y, (int)width, (int)DebugTabChrome.RowHeight));
            y += DebugTabChrome.RowHeight;
            if (_selectedTab is not null)
            {
                list.CanUserScrollVertical = true;
                DebugTabElements.Place(list, new Rectangle(0, (int)y, (int)width, (int)System.Math.Max(DebugTabChrome.RowHeight, height - y)));
                continue;
            }

            var rowCount = list.RowsInDisplayOrder.Count();
            if (rowCount == 0)
            {
                continue;
            }

            list.CanUserScrollVertical = false;
            var listHeight = LiveReportViews.WholeHeightOf(list, rowCount);
            DebugTabElements.Place(list, new Rectangle(0, (int)y, (int)width, (int)listHeight));
            y += listHeight + DebugTabChrome.RowGap;
        }
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
            entry.Detail,
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

    private Window CreateTabBar(Rectangle bounds)
    {
        var tabBar = services.Pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(bounds.X, bounds.Y), Size = new Vector2(bounds.Width, bounds.Height), MaximumSize = new Vector2(4000, 4000), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        tabBar.ContentPadding = Vector2.Zero;
        tabBar.Initialize();
        services.Layers.Add(UiLayer.Base, tabBar);
        return tabBar;
    }

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

    /// <summary>Kind and date, then the detail filling what is left of the browser's width.</summary>
    private IReadOnlyList<ListViewColumn> BrowserColumns() =>
        [new("Kind", KindColumnWidth), new("Date", DateColumnWidth), new("Detail", System.Math.Max(MinimumDetailColumnWidth, _browser.ContentSize.X - KindColumnWidth - DateColumnWidth - ListScrollbarAllowance))];

    private static float SecondToolbarRowTop => DebugTabChrome.RowHeight + DebugTabChrome.RowGap;

    private static float BaselineLabelLeft => ThresholdLabelWidth + ThresholdFieldWidth + DebugTabChrome.Margin;

    private static float ListTop => SecondToolbarRowTop + DebugTabChrome.RowHeight + DebugTabChrome.RowGap;

    private float BaselineLabelWidth() => System.Math.Max(1, _browser.ContentSize.X - BaselineLabelLeft);

    private Vector2 ListSize() =>
        new(_browser.ContentSize.X, System.Math.Max(DebugTabChrome.RowHeight, _browser.ContentSize.Y - ListTop));

    /// <summary>The tab row across the top, then the browser on the left and the opened report or comparison on the right.</summary>
    private static (Rectangle TabBar, Rectangle Browser, Rectangle Details) Areas(Rectangle bounds)
    {
        var columnsTop = bounds.Y + (int)(DebugTabChrome.RowHeight + DebugTabChrome.RowGap);
        var columnsHeight = System.Math.Max(100, bounds.Bottom - columnsTop);
        var browserWidth = (int)System.Math.Min(BrowserWidth, bounds.Width / 2f);
        var detailsLeft = bounds.X + browserWidth + (int)DebugTabChrome.Margin;
        return (
            new Rectangle(bounds.X, bounds.Y, bounds.Width, (int)DebugTabChrome.RowHeight),
            new Rectangle(bounds.X, columnsTop, browserWidth, columnsHeight),
            new Rectangle(detailsLeft, columnsTop, System.Math.Max(100, bounds.Right - detailsLeft), columnsHeight));
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
