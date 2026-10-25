using System.Globalization;
using Engine.Diagnostics;
using Microsoft.Xna.Framework;
using UIEngine.UI;

namespace Workshop.Debug;

/// <summary>What the Live section shows under its graphs: the gauges, or one of the latest reports' views.</summary>
public enum LiveView
{
    Gauges,
    FrameCosts,
    Memory,
    Leaks,
    Startup,
}

/// <summary>The Live section's report views -- frame costs, memory, leaks and startup -- each a ListView over the connected game's latest periodic or startup report.</summary>
/// <remarks>
/// Rows are rebuilt only when a new report arrives (every 5 s for the periodic one), never per frame. Frame costs open sorted
/// by milliseconds per second, costliest first, each with its change since the previous report -- and clicking one colours every cost measured inside it, lighter at each layer down (FrameCostEntry.Parents); memory by bytes. Startup is
/// the scope tree in start order, each phase indented under the one it ran inside, so it never sorts. A view whose report is
/// missing shows a line saying why -- the feature was off, or nothing has arrived yet -- in place of its rows.
/// </remarks>
public sealed class LiveReportViews(DebugTabServices services)
{
    private const string StartupIndent = "    ";

    /// <summary>How much of the layer above's highlight each layer of children keeps.</summary>
    private const float ChildHighlightFade = 0.6f;

    private readonly Dictionary<LiveView, ListView> _lists = [];
    private LiveDiagnosticsSession? _shownSession;
    private int _shownReportVersion = -1;
    private DiagnosticsStartupReport? _shownStartupReport;

    /// <summary>The frame cost selected, kept by name across reports, since each report's rows replace the last's.</summary>
    private FrameCostRowName? _selectedFrameCost;
    private bool _frameCostsArePerFrame;

    public ListView ListFor(LiveView view) => _lists[view];

    public void Create(Window parent)
    {
        _lists.Clear();
        _shownSession = null;
        _shownReportVersion = -1;
        _shownStartupReport = null;
        _selectedFrameCost = null;
        _lists[LiveView.FrameCosts] = CreateList(parent, FrameCostColumns(arePerFrame: false));
        _frameCostsArePerFrame = false;
        _lists[LiveView.FrameCosts].SelectionChanged += OnFrameCostSelected;
        _lists[LiveView.Memory] = CreateList(parent, [new("Component", 260), new("Holders", 90), new("MB", 80), new("Bytes/holder", 100), new("Population", 110)]);
        _lists[LiveView.Leaks] = CreateList(parent, [new("Subject", 200), new("Growth", 80), new("Detail", 400)], showIconColumn: true);
        _lists[LiveView.Startup] = CreateList(parent, [new("Phase", 520, Sortable: false), new("ms", 100, Sortable: false)]);
    }

    /// <summary>Shows view's list in bounds, scrolling its rows, when it has rows; hides every other list.</summary>
    public void Show(LiveView view, Rectangle bounds)
    {
        foreach (var (listView, list) in _lists)
        {
            list.IsVisible = listView == view && list.RowsInDisplayOrder.Any();
            if (listView == view)
            {
                list.CanUserScrollVertical = true;
                DebugTabElements.Place(list, bounds);
            }
        }
    }

    /// <summary>Hides every list, for a caller that then places some with ShowWhole.</summary>
    public void HideAll()
    {
        foreach (var list in _lists.Values)
        {
            list.IsVisible = false;
        }
    }

    /// <summary>Shows view's list at top, as tall as all its rows so its parent scrolls them, when it has rows; returns its height, 0 when it has none.</summary>
    public float ShowWhole(LiveView view, float top, float width)
    {
        var list = _lists[view];
        var rowCount = list.RowsInDisplayOrder.Count();
        list.IsVisible = rowCount > 0;
        if (rowCount == 0)
        {
            return 0;
        }

        list.CanUserScrollVertical = false;
        var height = WholeHeightOf(list, rowCount);
        DebugTabElements.Place(list, new Rectangle(0, (int)top, (int)width, (int)height));
        return height;
    }

    /// <summary>How tall list must be to show rowCount rows and its header without scrolling, border and padding included.</summary>
    public static float WholeHeightOf(ListView list, int rowCount) =>
        list.ContentInset.Y + ListView.HeaderHeight + rowCount * ListView.RowHeight;

    /// <summary>Changes whenever a list's rows are replaced -- what a caller showing them whole lays out again for.</summary>
    public int RowsVersion { get; private set; }

    /// <summary>Rebuilds the lists from session's reports when a new one has arrived. UI thread, once per frame.</summary>
    public void Update(LiveDiagnosticsSession? session)
    {
        var reportVersion = session?.PeriodicReportVersion ?? 0;
        if (!ReferenceEquals(session, _shownSession) || reportVersion != _shownReportVersion)
        {
            var isFirstReport = _shownReportVersion <= 0 || !ReferenceEquals(session, _shownSession);
            _shownReportVersion = reportVersion;
            var selectedFrameCost = _selectedFrameCost;
            ShowFrameCostUnit(session?.FrameCostsArePerFrame ?? false);
            SetRows(LiveView.FrameCosts, FrameCostRows(session?.LatestPeriodicReport, session?.PreviousPeriodicReport, session?.FrameCostsArePerFrame ?? false), isFirstReport ? 3 : null);
            var frameCosts = _lists[LiveView.FrameCosts];
            frameCosts.Select(frameCosts.RowsInDisplayOrder.FirstOrDefault(row => FrameCostNameOf(row) == selectedFrameCost), scrollIntoView: false);
            _selectedFrameCost = selectedFrameCost;
            SetRows(LiveView.Memory, MemoryRows(session?.LatestPeriodicReport), isFirstReport ? 2 : null);
            SetRows(LiveView.Leaks, LeakRows(session?.LatestPeriodicReport), null);
        }

        if (!ReferenceEquals(session, _shownSession) || !ReferenceEquals(session?.StartupReport, _shownStartupReport))
        {
            _shownStartupReport = session?.StartupReport;
            SetRows(LiveView.Startup, StartupRows(_shownStartupReport), null);
        }

        _shownSession = session;
    }

    /// <summary>The line shown over view: why it has no rows, or what its rows are.</summary>
    public static string MessageFor(LiveView view, LiveDiagnosticsSession? session)
    {
        if (session is null)
        {
            return "No game connected: press Play (F5).";
        }

        var features = session.Hello is { } hello && Enum.TryParse<DiagnosticsFeatures>(hello.Features, out var parsed) ? parsed : (DiagnosticsFeatures?)null;
        var feature = view switch
        {
            LiveView.FrameCosts => (DiagnosticsFeatures.FrameBudget, "Frame budget"),
            LiveView.Memory => (DiagnosticsFeatures.Memory, "Memory"),
            LiveView.Leaks => (DiagnosticsFeatures.LeakDetection, "Leaks"),
            LiveView.Startup => (DiagnosticsFeatures.Startup, "Startup"),
            _ => (DiagnosticsFeatures.Gauges, "Gauges"),
        };
        if (features is { } runFeatures && !runFeatures.HasFlag(feature.Item1))
        {
            return session.Source is not null
                ? NotAvailableText(view)
                : $"{feature.Item2} is off for this run: tick {feature.Item2} under Launch Settings, then Play again.";
        }

        if (view == LiveView.Startup)
        {
            return session.StartupReport is { } startup
                ? $"Startup: frame pacing settled after {startup.TimeToStableMilliseconds?.ToString("N0", CultureInfo.InvariantCulture) ?? "?"} ms."
                : "Waiting for frame pacing to settle: the startup report comes once it has.";
        }

        if (session.LatestPeriodicReport is not { } report)
        {
            return "Waiting for the first report: one comes every 5 seconds.";
        }

        var time = report.TimestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return view switch
        {
            LiveView.FrameCosts when session.FrameCostsArePerFrame => "Milliseconds per frame.",
            LiveView.FrameCosts => $"Milliseconds spent per second, from the report at {time}; change since the one before.",
            LiveView.Memory => $"Every component pool, from the report at {time}.",
            LiveView.Leaks => report.Leaks is { Count: > 0 } leaks ? $"{leaks.Count} possible leak(s) at {time}, also in the Error List." : $"No leak indicators at {time}.",
            _ => $"Report at {time}.",
        };
    }

    /// <summary>What a report read from a file says for a view it holds nothing for; the file's name is the viewer's title, so it isn't repeated.</summary>
    public static string NotAvailableText(LiveView view) => view switch
    {
        LiveView.FrameCosts => "No frame cost report available.",
        LiveView.Memory => "No memory report available.",
        LiveView.Leaks => "No leak report available.",
        LiveView.Startup => "No startup report available.",
        _ => "No gauge report available.",
    };

    private void SetRows(LiveView view, IReadOnlyList<ListViewRow> rows, int? sortDescendingByColumn)
    {
        var list = _lists[view];
        list.SetRows(rows);
        RowsVersion++;
        if (sortDescendingByColumn is not { } column)
        {
            return;
        }

        while (list.SortColumnIndex != column || list.SortAscending)
        {
            list.SortBy(column);
        }
    }

    /// <summary>The frame costs' columns: milliseconds per second for a game's report, per frame for a benchmark's.</summary>
    private static IReadOnlyList<ListViewColumn> FrameCostColumns(bool arePerFrame) =>
        [new("Category", 70), new("Group", 150), new("Item", 260), new(arePerFrame ? "ms/frame" : "ms/s", 80), new("Change", 80)];

    /// <summary>Retitles the frame costs' value column when the session's unit differs from the one shown, which resets its sort.</summary>
    private void ShowFrameCostUnit(bool arePerFrame)
    {
        if (arePerFrame == _frameCostsArePerFrame)
        {
            return;
        }

        _frameCostsArePerFrame = arePerFrame;
        _lists[LiveView.FrameCosts].SetColumns(FrameCostColumns(arePerFrame), showIconColumn: false);
    }

    private static List<ListViewRow> FrameCostRows(DiagnosticsReport? report, DiagnosticsReport? previous, bool arePerFrame)
    {
        if (report is null)
        {
            return [];
        }

        var previousCosts = previous is null
            ? []
            : DiagnosticsReportReader.FrameCostsOf(previous).ToDictionary(static entry => (entry.Category, entry.GroupName, entry.ItemName), static entry => entry.MillisecondsPerSecond);
        return
        [
            .. DiagnosticsReportReader.FrameCostsOf(report).Select(entry => new ListViewRow(
            [
                entry.Category.ToString(),
                entry.GroupName,
                entry.ItemName,
                entry.MillisecondsPerSecond.ToString(arePerFrame ? "F4" : "F1", CultureInfo.InvariantCulture),
                previous is null ? string.Empty
                    : previousCosts.TryGetValue((entry.Category, entry.GroupName, entry.ItemName), out var before) ? (entry.MillisecondsPerSecond - before).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)
                    : "new",
            ],
            Tag: entry)),
        ];
    }

    /// <summary>The highlight of a frame cost depth layers below the one selected: lighter at each layer than the selection's own.</summary>
    public static Color ChildHighlightColor(int depth) => Color.Gold * (0.5f * MathF.Pow(ChildHighlightFade, depth));

    private static FrameCostRowName NameOf(FrameCostEntry entry) => new(entry.Category, entry.GroupName, entry.ItemName);

    private static FrameCostRowName? FrameCostNameOf(ListViewRow? row) => row?.Tag is FrameCostEntry entry ? NameOf(entry) : null;

    private void OnFrameCostSelected(ListViewRow? row)
    {
        _selectedFrameCost = FrameCostNameOf(row);
        HighlightFrameCostChildren();
    }

    /// <summary>Colours every row the selected frame cost ran around -- its children, theirs, and so on -- lighter at each layer; a row reached at two depths takes the nearer.</summary>
    private void HighlightFrameCostChildren()
    {
        var list = _lists[LiveView.FrameCosts];
        if (_selectedFrameCost is not { } selected)
        {
            list.SetRowColors([]);
            return;
        }

        var childrenByParent = new Dictionary<FrameCostRowName, List<ListViewRow>>();
        foreach (var row in list.RowsInDisplayOrder)
        {
            if (row.Tag is not FrameCostEntry { Parents: { } parents })
            {
                continue;
            }

            foreach (var parent in parents)
            {
                if (!childrenByParent.TryGetValue(parent, out var children))
                {
                    children = [];
                    childrenByParent[parent] = children;
                }

                children.Add(row);
            }
        }

        var colors = new Dictionary<ListViewRow, Color>(ReferenceEqualityComparer.Instance);
        var reached = new HashSet<FrameCostRowName> { selected };
        var layer = new List<FrameCostRowName> { selected };
        for (var depth = 1; layer.Count > 0; depth++)
        {
            var nextLayer = new List<FrameCostRowName>();
            foreach (var name in layer)
            {
                foreach (var child in childrenByParent.GetValueOrDefault(name) ?? [])
                {
                    var childName = NameOf((FrameCostEntry)child.Tag!);
                    if (reached.Add(childName))
                    {
                        colors[child] = ChildHighlightColor(depth);
                        nextLayer.Add(childName);
                    }
                }
            }

            layer = nextLayer;
        }

        list.SetRowColors(colors);
    }

    private static List<ListViewRow> MemoryRows(DiagnosticsReport? report) =>
        report?.Memory is not { } memory
            ? []
            : [.. memory.Select(static item => new ListViewRow(
            [
                item.ComponentType,
                item.Count.ToString(CultureInfo.InvariantCulture),
                (item.EstimatedBytes / (1024.0 * 1024.0)).ToString("F2", CultureInfo.InvariantCulture),
                item.Count == 0 ? string.Empty : (item.EstimatedBytes / (double)item.Count).ToString("F0", CultureInfo.InvariantCulture),
                item.Population ?? string.Empty,
            ]))];

    private static List<ListViewRow> LeakRows(DiagnosticsReport? report) =>
        report?.Leaks is not { } leaks
            ? []
            : [.. leaks.Select(static leak => new ListViewRow([leak.Subject, leak.GrowthRatio.ToString("P0", CultureInfo.InvariantCulture), leak.Detail], "!", Color.DarkGoldenrod))];

    private static List<ListViewRow> StartupRows(DiagnosticsStartupReport? report) =>
        report is null
            ? []
            : [.. report.Phases.Select(static phase => new ListViewRow([string.Concat(Enumerable.Repeat(StartupIndent, phase.Depth)) + phase.Name, phase.Milliseconds.ToString("F1", CultureInfo.InvariantCulture)]))];

    private ListView CreateList(Window parent, IReadOnlyList<ListViewColumn> columns, bool showIconColumn = false)
    {
        var list = DebugTabElements.CreateChild<ListView>(services, parent, Vector2.Zero, Vector2.One, showBorder: true);
        list.SetColumns(columns, showIconColumn);
        list.IsVisible = false;
        return list;
    }
}
