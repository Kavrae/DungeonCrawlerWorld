using System.Globalization;
using Engine.Diagnostics;
using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine;
using UIEngine.Input;
using UIEngine.UI;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace Workshop.Debug;

/// <summary>The Debug tab's Live section: the connected game's state, then a switch between its gauges -- the frame graph with the selected gauges under it, over every gauge with its window's statistics -- and the latest reports' views.</summary>
/// <remarks>
/// <para>
/// Under the header a switch picks what fills the rest: the gauges, or one of the latest reports' views -- frame costs,
/// memory, leaks, startup (LiveReportViews) -- with a line over it saying what it shows, or why it is empty. The game's pid
/// and seed sit in read-only boxes, to copy. A report's viewer has no switch (hasViewSwitch false): the Reports page's tabs
/// call ShowView for one view, or ShowViews to stack several, each under its line and as tall as its rows, the window
/// scrolling them.
/// </para>
/// <para>
/// The Gauges view is the frame graph over the gauge list, both drawn from the session's per-frame history, so a report with
/// none (a benchmark, memory or startup report) shows only its list's message there. The graphs show a window of the history
/// -- the last 5 s, 30 s, 2 min or 10 min -- ending at its newest frame, or at the frame Freeze stopped at (Freeze or Space,
/// while the view shows; the session keeps recording). A click on the timeline selects a frame and a drag a range, read out
/// beside the window choice; a right-click clears it. Selecting a gauge's row draws it large under the frame graph, on the
/// same axis.
/// </para>
/// <para>
/// Given the shell's WorkshopDebugging (the Live section, not a report's viewer), a row under the header describes the last
/// launch: Stop while it runs, its progress toward the benchmark range's end when it measures one -- a headless run has no
/// window to show it -- and the command line it was started with, which editing the options doesn't change.
/// </para>
/// <para>
/// Text is rebuilt only when what it shows changes, at the precision shown: the header and the gauge rows every
/// DebugTabChrome.TextRefreshFrames records (or when the window, selection or session changes), the readout when the selection
/// does. The graphs redraw every frame from the history.
/// </para>
/// </remarks>
public sealed class DebugLiveSection(DebugTabServices services, string title = "Live", string idleText = DebugLiveSection.WaitingForPlayText, WorkshopDebugging? debugging = null, bool hasViewSwitch = true)
{
    /// <summary>The window lengths offered, in frames at 60 per second.</summary>
    public static readonly (string Label, int Frames)[] WindowLengths = [("Last 5 s", 300), ("Last 30 s", 1800), ("Last 2 min", 7200), ("Last 10 min", 36000)];

    private const int DefaultWindowLengthIndex = 1;
    private const float WindowChoiceWidth = 130f;
    private const float FreezeWidth = 90f;
    private const float MinimumGaugeListHeight = 120f;
    private const float ViewTileWidth = 100f;
    private const float IdentityLabelWidth = 40f;
    private const float ProcessIdBoxWidth = 90f;
    private const float SeedBoxWidth = 120f;
    private const float StopButtonWidth = 70f;
    private const float ProgressBarWidth = 200f;
    private const float ProgressTextWidth = 170f;
    private const float LaunchedLabelWidth = 70f;
    private const string GaugesHint = "Click a gauge to graph it under the frame graph; click it again to take it off.";

    private static readonly (LiveView View, string Label)[] Views =
    [
        (LiveView.Gauges, "Gauges"),
        (LiveView.FrameCosts, "Frame costs"),
        (LiveView.Memory, "Memory"),
        (LiveView.Leaks, "Leaks"),
        (LiveView.Startup, "Startup"),
    ];

    /// <summary>What the Live section says with no game connected.</summary>
    public const string WaitingForPlayText = "Waiting for Play (F5): the game Play starts connects here.";

    private readonly string _idleText = idleText;
    private readonly LiveReportViews _reportViews = new(services);
    private readonly List<(LiveView View, TextWindow Tile)> _viewTiles = [];
    private readonly Dictionary<LiveView, TextWindow> _viewMessages = [];
    private Rectangle _viewBounds;
    private float _viewsTop;
    private IReadOnlyList<LiveView>? _stackedViews;
    private (int RowsVersion, float GaugeRowsHeight, int SelectedGaugeCount) _stackLaidOutFor;

    private readonly KeyedCache<HeaderKey, string> _connectionText = new();
    private readonly KeyedCache<HeaderKey, string> _headerText = new();
    private readonly KeyedCache<ReadoutKey, string> _readoutText = new();
    private readonly KeyedCache<(long FrameBucket, BenchmarkFrameRange Range), string> _progressText = new();
    private readonly List<string> _bandLabels = [];
    private double[] _windowScratch = [];
    private double[][] _sparklines = [];
    private GaugeRowText[] _rowTexts = [];
    private TextWindow _processIdLabel = null!;
    private TextBox _processIdBox = null!;
    private TextWindow _seedLabel = null!;
    private TextBox _seedBox = null!;
    private TextWindow _connection = null!;
    private TextWindow _header = null!;
    private DropdownField _windowChoice = null!;
    private Toggle _freeze = null!;
    private TextWindow _readout = null!;
    private DiagnosticsTimelineView _timeline = null!;
    private DiagnosticsGaugeList _gaugeList = null!;
    private Button? _stop;
    private FractionBarElement? _progressBar;
    private TextWindow? _progressLabel;
    private TextWindow? _launchedLabel;
    private TextBox? _launchedCommandLine;
    private LiveDiagnosticsSession? _shownSession;
    private IReadOnlyList<DiagnosticsLinkGaugeDescriptor>? _shownGauges;
    private RefreshKey _shownRefresh;
    private string _frameLabel = string.Empty;
    private int _selectionVersion;
    private long _frozenEndRecord;

    public Window Window { get; private set; } = null!;

    public DiagnosticsTimelineView Timeline => _timeline;

    public DiagnosticsGaugeList GaugeList => _gaugeList;

    public Toggle Freeze => _freeze;

    public DropdownField WindowChoice => _windowChoice;

    /// <summary>The view shown alone; while views are stacked (ShowViews), the one shown alone next.</summary>
    public LiveView View { get; private set; } = LiveView.Gauges;

    /// <summary>The views shown, in order: View alone, or every view ShowViews stacked.</summary>
    public IReadOnlyList<LiveView> ShownViews => _stackedViews ?? [View];

    public LiveReportViews ReportViews => _reportViews;

    /// <summary>view's tile on the switch under the header; only with hasViewSwitch.</summary>
    public TextWindow TileFor(LiveView view) => _viewTiles.First(entry => entry.View == view).Tile;

    /// <summary>The line over View, as shown.</summary>
    public string ViewMessageText => _viewMessages[View].OriginalText;

    /// <summary>The line over view, as shown; empty when view isn't shown.</summary>
    public string MessageTextFor(LiveView view) => _viewMessages[view].IsVisible ? _viewMessages[view].OriginalText : string.Empty;

    /// <summary>The game's process id as shown, in its read-only box.</summary>
    public TextBox ProcessIdBox => _processIdBox;

    /// <summary>The game's seed as shown, in its read-only box.</summary>
    public TextBox SeedBox => _seedBox;

    /// <summary>The connection line beside them, as shown.</summary>
    public string ConnectionText => _connection.OriginalText;

    /// <summary>The header as shown.</summary>
    public string HeaderText => _header.OriginalText;

    /// <summary>Stop, on the launch row; null for a report's viewer.</summary>
    public Button? StopButton => _stop;

    /// <summary>The launch's progress bar; null for a report's viewer.</summary>
    public FractionBarElement? ProgressBar => _progressBar;

    /// <summary>How far the launch's benchmark has got, as shown beside its bar; empty when it measures none.</summary>
    public string ProgressText => _progressLabel?.OriginalText ?? string.Empty;

    /// <summary>How much of the launch's benchmark range has run, 0 to 1; 0 when it measures none.</summary>
    public float ProgressFraction { get; private set; }

    /// <summary>The command line the last launch started with, as shown; empty for a report's viewer.</summary>
    public string LaunchedCommandLineText => _launchedCommandLine?.OriginalText ?? string.Empty;

    /// <summary>The cursor readout as shown.</summary>
    public string ReadoutText => _readout.OriginalText;

    /// <summary>The record the graphs end at: the newest, or the one Freeze stopped at.</summary>
    public long WindowEndRecord { get; private set; }

    public int WindowFrames => WindowLengths[System.Math.Max(0, _windowChoice.SelectedIndex)].Frames;

    public void Open(Rectangle bounds)
    {
        var font = services.Fonts.GetFont(FontChrome.DefaultFontSize);
        _shownSession = null;
        _shownGauges = null;
        _shownRefresh = default;
        _frozenEndRecord = 0;
        Window = DebugTabElements.CreateWindow(services, bounds, title);
        _processIdLabel = DebugTabElements.CreateText(services, Window, Vector2.Zero, new Vector2(IdentityLabelWidth, DebugTabChrome.RowHeight), "pid");
        _processIdBox = DebugTabElements.CreateReadOnlyBox(services, Window, Vector2.Zero, new Vector2(ProcessIdBoxWidth, DebugTabChrome.RowHeight));
        _seedLabel = DebugTabElements.CreateText(services, Window, Vector2.Zero, new Vector2(IdentityLabelWidth, DebugTabChrome.RowHeight), "seed");
        _seedBox = DebugTabElements.CreateReadOnlyBox(services, Window, Vector2.Zero, new Vector2(SeedBoxWidth, DebugTabChrome.RowHeight));
        _connection = DebugTabElements.CreateText(services, Window, Vector2.Zero, Vector2.One, string.Empty);
        _header = DebugTabElements.CreateText(services, Window, Vector2.Zero, Vector2.One, string.Empty);
        CreateLaunchRow();
        _windowChoice = DebugTabElements.CreateChild<DropdownField>(services, Window, Vector2.Zero, new Vector2(WindowChoiceWidth, DebugTabChrome.RowHeight), showBorder: true);
        _windowChoice.SetOptions([.. WindowLengths.Select(length => length.Label)], DefaultWindowLengthIndex);
        _freeze = services.Pool.CreateElement<Toggle>(Window, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(FreezeWidth, DebugTabChrome.RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        _freeze.Configure("Freeze", LabelPosition.East, defaultOn: false, DebugTabChrome.ToggleOn, WindowPalette.ControlBackground, WindowPalette.BodyTextColor, font, OnFreezeToggled);
        Window.AddChild(_freeze);
        _readout = DebugTabElements.CreateText(services, Window, Vector2.Zero, Vector2.One, string.Empty);
        _timeline = DebugTabElements.CreateChild<DiagnosticsTimelineView>(services, Window, Vector2.Zero, Vector2.One, showBorder: false);
        _gaugeList = DebugTabElements.CreateChild<DiagnosticsGaugeList>(services, Window, Vector2.Zero, Vector2.One, showBorder: true);
        _gaugeList.SelectionChanged += OnGaugeSelectionChanged;
        _viewTiles.Clear();
        _viewMessages.Clear();
        foreach (var (view, label) in Views)
        {
            if (hasViewSwitch)
            {
                _viewTiles.Add((view, DebugTabElements.CreateTile(services, Window, Vector2.Zero, ViewTileWidth, label, () => ShowView(view))));
            }

            _viewMessages[view] = DebugTabElements.CreateText(services, Window, Vector2.Zero, Vector2.One, string.Empty);
        }

        _reportViews.Create(Window);
        services.AddInputHandler(new TimelineSelectionInputHandler(_timeline));
        services.AddInputHandler(new KeyPressInputHandler(Microsoft.Xna.Framework.Input.Keys.Space, ToggleFreezeWhileShown));
        if (_stackedViews is { } stackedViews)
        {
            ShowViews(stackedViews);
        }
        else
        {
            ShowView(View);
        }
    }

    /// <summary>Shows view alone, filling the area under the header.</summary>
    public void ShowView(LiveView view)
    {
        View = view;
        _stackedViews = null;
        foreach (var (tileView, tile) in _viewTiles)
        {
            DebugTabElements.StyleTile(tile, isSelected: tileView == view);
        }

        Window.CanUserScrollVertical = false;
        Window.ScrollTo(Vector2.Zero);
        LayOut();
        ShowViewContent();
    }

    /// <summary>Shows every one of views in turn, each as tall as its rows, under its own line; the window scrolls them.</summary>
    public void ShowViews(IReadOnlyList<LiveView> views)
    {
        _stackedViews = views;
        Window.CanUserScrollVertical = true;
        Window.ScrollTo(Vector2.Zero);
        LayOut();
        ShowViewContent();
    }

    public void Resize(Rectangle bounds)
    {
        DebugTabElements.Place(Window, bounds);
        LayOut();
    }

    /// <summary>Shows session -- the one the listener has -- as of now. UI thread, once per frame.</summary>
    public void Update(LiveDiagnosticsSession? session)
    {
        if (!ReferenceEquals(session, _shownSession))
        {
            _shownSession = session;
            _frozenEndRecord = 0;
            _freeze.SetIsOn(false);
        }

        var gauges = session?.Gauges ?? [];
        if (!ReferenceEquals(gauges, _shownGauges))
        {
            _shownGauges = gauges;
            _gaugeList.SetGauges(gauges);
        }

        _gaugeList.Message = session is null ? _idleText : MessageFor(session);
        var history = session?.History;
        WindowEndRecord = _freeze.IsOn ? _frozenEndRecord : history?.TotalRecordedCount ?? 0;
        var headerKey = HeaderKey.Of(session);
        _connection.UpdateText(_connectionText.Get(headerKey, this, BuildConnection));
        _header.UpdateText(_headerText.Get(headerKey, session, BuildHeader));
        UpdateLaunchRow();
        ShowInBox(_processIdBox, session?.Hello?.ProcessId.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        ShowInBox(_seedBox, session?.Hello?.RandomSeed?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

        var refresh = new RefreshKey(history, WindowEndRecord / DebugTabChrome.TextRefreshFrames, WindowFrames, _selectionVersion);
        if (refresh != _shownRefresh)
        {
            _shownRefresh = refresh;
            RebuildWindowText(history, gauges);
        }

        _timeline.Show(history, gauges, WindowEndRecord, WindowFrames, _gaugeList.SelectedGaugeIndexes, _bandLabels, _frameLabel);
        _readout.UpdateText(_readoutText.Get(new ReadoutKey(_timeline.SelectionVersion, _selectionVersion, WindowEndRecord / DebugTabChrome.TextRefreshFrames, _freeze.IsOn), this, BuildReadout));
        _reportViews.Update(session);
        ShowViewContent();
    }

    /// <summary>Each shown view's line and its content -- the frame graph over the gauge list, or a report's rows -- in the area under the header; stacked views are laid out again when their rows change.</summary>
    private void ShowViewContent()
    {
        var shownViews = ShownViews;
        var showsGauges = shownViews.Contains(LiveView.Gauges);
        _windowChoice.IsVisible = showsGauges;
        _freeze.IsVisible = showsGauges;
        _readout.IsVisible = showsGauges;
        _timeline.IsVisible = showsGauges;
        _gaugeList.IsVisible = showsGauges;
        if (_stackedViews is null)
        {
            if (View == LiveView.Gauges)
            {
                _reportViews.HideAll();
            }
            else
            {
                _reportViews.Show(View, _viewBounds);
            }
        }
        else if (StackLayoutKey != _stackLaidOutFor)
        {
            LayOut();
        }

        foreach (var (view, label) in Views)
        {
            var message = _viewMessages[view];
            message.IsVisible = shownViews.Contains(view);
            if (!message.IsVisible)
            {
                continue;
            }

            var text = view == LiveView.Gauges
                ? _gaugeList.SelectedGaugeIndexes.Count == 0 && (_shownGauges?.Count ?? 0) > 0 ? GaugesHint : string.Empty
                : _shownSession is null ? _idleText : LiveReportViews.MessageFor(view, _shownSession);
            message.UpdateText(_stackedViews is null ? text : text.Length == 0 ? label : $"{label}: {text}");
        }
    }

    /// <summary>What the stacked views' heights are made from.</summary>
    private (int RowsVersion, float GaugeRowsHeight, int SelectedGaugeCount) StackLayoutKey => (_reportViews.RowsVersion, _gaugeList.RowsHeight, _gaugeList.SelectedGaugeIndexes.Count);

    /// <summary>Stop, the progress bar and its text, and the launched command line -- only for the Live section, which is given the shell's WorkshopDebugging.</summary>
    private void CreateLaunchRow()
    {
        _stop = null;
        _progressBar = null;
        _progressLabel = null;
        _launchedLabel = null;
        _launchedCommandLine = null;
        if (debugging is null)
        {
            return;
        }

        _stop = services.Pool.CreateElement<Button>(Window, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(StopButtonWidth, DebugTabChrome.RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.ControlBackground },
            Text = new TextOptions { Text = "Stop", TextColor = WindowPalette.ControlLabelTextColor },
        });
        _stop.Clicked += _ => OnStopClicked();
        Window.AddChild(_stop);
        _progressBar = DebugTabElements.CreateChild<FractionBarElement>(services, Window, Vector2.Zero, new Vector2(ProgressBarWidth, DebugTabChrome.RowHeight), showBorder: false);
        _progressLabel = DebugTabElements.CreateText(services, Window, Vector2.Zero, new Vector2(ProgressTextWidth, DebugTabChrome.RowHeight), string.Empty);
        _launchedLabel = DebugTabElements.CreateText(services, Window, Vector2.Zero, new Vector2(LaunchedLabelWidth, DebugTabChrome.RowHeight), "Launched");
        _launchedCommandLine = DebugTabElements.CreateReadOnlyBox(services, Window, Vector2.Zero, new Vector2(1, DebugTabChrome.RowHeight));
    }

    private void OnStopClicked()
    {
        if (debugging is not null && debugging.Stop(out var status))
        {
            services.Status.Show(status);
        }
    }

    /// <summary>Shows what the last launch is doing: Stop enabled while it runs, its benchmark's progress, its command line.</summary>
    private void UpdateLaunchRow()
    {
        if (debugging is null || _stop is null || _progressBar is null || _progressLabel is null || _launchedCommandLine is null)
        {
            return;
        }

        _stop.Enabled = debugging.IsGameRunning;
        ShowInBox(_launchedCommandLine, debugging.LaunchedCommandLine);
        if (debugging.LaunchedOptions is not { RunsBenchmark: true } launched)
        {
            ProgressFraction = 0;
            _progressBar.IsVisible = false;
            _progressLabel.UpdateText(string.Empty);
            return;
        }

        var range = launched.BenchmarkFrames;
        var frame = debugging.LaunchedSession?.LatestFrame ?? 0;
        ProgressFraction = (float)System.Math.Clamp((double)(frame - range.StartFrame) / (range.EndFrame - range.StartFrame), 0, 1);
        _progressBar.IsVisible = true;
        _progressBar.Configure(ProgressFraction, hasResource: true, DebugTabChrome.GraphBackground, static _ => DebugTabChrome.FrameSeries);
        _progressLabel.UpdateText(_progressText.Get((frame / DebugTabChrome.TextRefreshFrames, range), frame, static (key, latestFrame) => WorkshopDebugging.ProgressText(latestFrame, key.Range)));
    }

    private void OnFreezeToggled(bool isOn)
    {
        _frozenEndRecord = _shownSession?.History?.TotalRecordedCount ?? 0;
        _shownRefresh = default;
    }

    private void OnGaugeSelectionChanged()
    {
        _selectionVersion++;
        LayOut();
    }

    private void LayOut()
    {
        var width = Window.ContentSize.X;
        var height = Window.ContentSize.Y;
        var y = 0f;
        var x = 0f;
        _processIdLabel.SetRelativePosition(new Vector2(x, 0));
        x += IdentityLabelWidth;
        _processIdBox.SetRelativePosition(new Vector2(x, 0));
        x += ProcessIdBoxWidth + DebugTabChrome.Margin;
        _seedLabel.SetRelativePosition(new Vector2(x, 0));
        x += IdentityLabelWidth;
        _seedBox.SetRelativePosition(new Vector2(x, 0));
        x += SeedBoxWidth + DebugTabChrome.Margin;
        DebugTabElements.Place(_connection, new Rectangle((int)x, 0, (int)System.Math.Max(1, width - x), (int)DebugTabChrome.RowHeight));
        DebugTabElements.Place(_header, new Rectangle(0, (int)DebugTabChrome.RowHeight, (int)width, (int)(DebugTabChrome.HeaderHeight - DebugTabChrome.RowHeight)));
        y += DebugTabChrome.HeaderHeight;
        if (_stop is not null && _progressBar is not null && _progressLabel is not null && _launchedLabel is not null && _launchedCommandLine is not null)
        {
            x = 0f;
            _stop.SetRelativePosition(new Vector2(x, y));
            x += StopButtonWidth + DebugTabChrome.Margin;
            _progressBar.SetRelativePosition(new Vector2(x, y));
            x += ProgressBarWidth + DebugTabChrome.RowGap;
            _progressLabel.SetRelativePosition(new Vector2(x, y));
            x += ProgressTextWidth + DebugTabChrome.Margin;
            _launchedLabel.SetRelativePosition(new Vector2(x, y));
            x += LaunchedLabelWidth;
            DebugTabElements.Place(_launchedCommandLine, new Rectangle((int)x, (int)y, (int)System.Math.Max(1, width - x), (int)DebugTabChrome.RowHeight));
            y += DebugTabChrome.RowHeight + DebugTabChrome.RowGap;
        }

        if (hasViewSwitch)
        {
            x = 0f;
            foreach (var (_, tile) in _viewTiles)
            {
                tile.SetRelativePosition(new Vector2(x, y));
                x += ViewTileWidth;
            }

            y += DebugTabChrome.RowHeight + DebugTabChrome.RowGap;
        }

        _viewsTop = y;
        if (_stackedViews is { } stackedViews)
        {
            LayOutStackedViews(stackedViews, width);
            return;
        }

        PlaceMessage(View, ref y, width);
        _viewBounds = new Rectangle(0, (int)y, (int)width, (int)System.Math.Max(DebugTabChrome.GaugeListRowHeight * 2, height - y));
        if (View != LiveView.Gauges)
        {
            _reportViews.Show(View, _viewBounds);
        }

        PlaceGaugeControls(ref y, width);
        var timelineHeight = System.Math.Min(DiagnosticsTimelineView.HeightFor(_gaugeList.SelectedGaugeIndexes.Count), System.Math.Max(DebugTabChrome.FrameBandHeight, height - y - MinimumGaugeListHeight - DebugTabChrome.RowGap));
        DebugTabElements.Place(_timeline, new Rectangle(0, (int)y, (int)width, (int)timelineHeight));
        y += timelineHeight + DebugTabChrome.RowGap;
        _gaugeList.CanUserScrollVertical = true;
        DebugTabElements.Place(_gaugeList, new Rectangle(0, (int)y, (int)width, (int)System.Math.Max(DebugTabChrome.GaugeListRowHeight * 2, height - y)));
    }

    /// <summary>Each of views in turn under its line, as tall as everything it shows, so the window scrolls them rather than any one of them.</summary>
    private void LayOutStackedViews(IReadOnlyList<LiveView> views, float width)
    {
        _stackLaidOutFor = StackLayoutKey;
        _reportViews.HideAll();
        var y = _viewsTop;
        foreach (var view in views)
        {
            PlaceMessage(view, ref y, width);
            if (view != LiveView.Gauges)
            {
                var listHeight = _reportViews.ShowWhole(view, y, width);
                y += listHeight > 0 ? listHeight + DebugTabChrome.RowGap : 0;
                continue;
            }

            PlaceGaugeControls(ref y, width);
            var timelineHeight = DiagnosticsTimelineView.HeightFor(_gaugeList.SelectedGaugeIndexes.Count);
            DebugTabElements.Place(_timeline, new Rectangle(0, (int)y, (int)width, (int)timelineHeight));
            y += timelineHeight + DebugTabChrome.RowGap;
            _gaugeList.CanUserScrollVertical = false;
            _gaugeList.ScrollTo(Vector2.Zero);
            var gaugeListHeight = _gaugeList.ContentInset.Y + System.Math.Max(DebugTabChrome.GaugeListRowHeight * 2, _gaugeList.RowsHeight);
            DebugTabElements.Place(_gaugeList, new Rectangle(0, (int)y, (int)width, (int)gaugeListHeight));
            y += gaugeListHeight + DebugTabChrome.RowGap;
        }
    }

    private void PlaceMessage(LiveView view, ref float y, float width)
    {
        DebugTabElements.Place(_viewMessages[view], new Rectangle(0, (int)y, (int)width, (int)DebugTabChrome.RowHeight));
        y += DebugTabChrome.RowHeight;
    }

    /// <summary>The window length, Freeze and the readout, in a row at y.</summary>
    private void PlaceGaugeControls(ref float y, float width)
    {
        _windowChoice.SetRelativePosition(new Vector2(0, y));
        _freeze.SetRelativePosition(new Vector2(WindowChoiceWidth + DebugTabChrome.Margin, y));
        var readoutLeft = WindowChoiceWidth + FreezeWidth + DebugTabChrome.Margin * 2;
        DebugTabElements.Place(_readout, new Rectangle((int)readoutLeft, (int)y, (int)System.Math.Max(1, width - readoutLeft), (int)DebugTabChrome.RowHeight));
        y += DebugTabChrome.RowHeight + DebugTabChrome.RowGap;
    }

    /// <summary>Formats every gauge's row over the window, fills its sparkline, and labels the frame band and the selected gauges' bands.</summary>
    private void RebuildWindowText(DiagnosticsFrameHistory? history, IReadOnlyList<DiagnosticsLinkGaugeDescriptor> gauges)
    {
        var windowFrames = WindowFrames;
        if (_windowScratch.Length < windowFrames)
        {
            _windowScratch = new double[windowFrames];
        }

        if (_sparklines.Length != gauges.Count)
        {
            _sparklines = [.. Enumerable.Range(0, gauges.Count).Select(_ => new double[DebugTabChrome.GaugeSparklineWidth])];
        }

        _rowTexts = new GaugeRowText[gauges.Count];
        var sparklinePoints = 0;
        for (var gaugeIndex = 0; gaugeIndex < gauges.Count; gaugeIndex++)
        {
            var count = history?.CopyWindow(gaugeIndex + 1, WindowEndRecord, windowFrames, _windowScratch) ?? 0;
            var window = _windowScratch.AsSpan(0, count);
            var statistics = GaugeWindowStatistics.Of(window);
            var unit = gauges[gaugeIndex].Unit;
            _rowTexts[gaugeIndex] = count == 0
                ? new GaugeRowText("-", "-", "-", "-")
                : new GaugeRowText(GaugeValueFormatting.Format(unit, window[^1]), GaugeValueFormatting.Format(unit, statistics.Minimum), GaugeValueFormatting.Format(unit, statistics.Maximum), GaugeValueFormatting.Format(unit, statistics.Average));
            sparklinePoints = MaximumPool(window, _sparklines[gaugeIndex]);
        }

        _gaugeList.SetWindow(_rowTexts, _sparklines, sparklinePoints);

        _bandLabels.Clear();
        foreach (var gaugeIndex in _gaugeList.SelectedGaugeIndexes)
        {
            var gauge = gauges[gaugeIndex];
            var text = gaugeIndex < _rowTexts.Length ? _rowTexts[gaugeIndex] : null;
            _bandLabels.Add(text is null ? $"{gauge.Group}/{gauge.Name}" : $"{gauge.Group}/{gauge.Name}   {text.Minimum} - {text.Maximum}");
        }

        var updateCount = history?.CopyWindow(0, WindowEndRecord, windowFrames, _windowScratch) ?? 0;
        var updateStatistics = GaugeWindowStatistics.Of(_windowScratch.AsSpan(0, updateCount));
        _frameLabel = updateCount == 0
            ? "Update ms"
            : $"Update ms   avg {GaugeValueFormatting.Format(GaugeUnit.Milliseconds, updateStatistics.Average)}   max {GaugeValueFormatting.Format(GaugeUnit.Milliseconds, updateStatistics.Maximum)}";
    }

    /// <summary>Fits values to every point of destination -- squeezed, each point the largest of its slice so a spike survives, or stretched, each value repeated -- and returns how many points it wrote: all of them, or none for no values.</summary>
    private static int MaximumPool(ReadOnlySpan<double> values, double[] destination)
    {
        var pointCount = values.IsEmpty ? 0 : destination.Length;
        for (var point = 0; point < pointCount; point++)
        {
            var first = (int)((long)point * values.Length / pointCount);
            var end = System.Math.Max(first + 1, (int)((long)(point + 1) * values.Length / pointCount));
            var value = values[first];
            for (var index = first + 1; index < end; index++)
            {
                value = System.Math.Max(value, values[index]);
            }

            destination[point] = value;
        }

        return pointCount;
    }

    private static string MessageFor(LiveDiagnosticsSession session)
    {
        if (FeaturesOf(session) is { } features && !features.HasFlag(DiagnosticsFeatures.Gauges))
        {
            return session.Source is not null ? LiveReportViews.NotAvailableText(LiveView.Gauges) : "Gauges is off for this run: tick Gauges under Launch Settings, then Play again.";
        }

        return session.State == DiagnosticsConnectionState.Ended ? "The game ended before its session started." : "Waiting for the game's session to start.";
    }

    private static DiagnosticsFeatures? FeaturesOf(LiveDiagnosticsSession session) =>
        session.Hello is { } hello && Enum.TryParse<DiagnosticsFeatures>(hello.Features, out var features) ? features : null;

    /// <summary>The line beside the pid and seed: whether the game is connected, and frames dropped; nothing for a report read from a file, whose name is the window's title.</summary>
    private static string BuildConnection(HeaderKey key, DebugLiveSection section)
    {
        if (section._shownSession is not { } session)
        {
            return section._idleText;
        }

        if (session.Source is not null)
        {
            return string.Empty;
        }

        var connection = key.State == DiagnosticsConnectionState.Ended ? "Ended" : "Connected";
        return key.DroppedFrameCount > 0 ? $"{connection}   dropped {key.DroppedFrameCount:N0} frames" : connection;
    }

    /// <summary>The lines under the pid and seed: the run's features, then its frame, rates and pause.</summary>
    private static string BuildHeader(HeaderKey key, LiveDiagnosticsSession? session)
    {
        if (session is null)
        {
            return string.Empty;
        }

        var features = session.Hello is { } hello ? $"Features: {hello.Features}" : "Features: (waiting for the game)";
        if (session.Source is not null)
        {
            return session.History is { RecordedFrameCount: > 0 } history
                ? $"{features}\nFrames {history.LatestFrame - history.RecordedFrameCount + 1:N0}-{history.LatestFrame:N0}"
                : features;
        }

        var paused = key.IsPaused ? "   Paused" : string.Empty;
        return $"{features}\nFrame {key.LatestFrame:N0}   {key.UpdateRate:N0} ups   {key.DrawRate:N0} fps{paused}";
    }

    /// <summary>Shows text in box, leaving it alone -- and any selection in it -- when it already shows that.</summary>
    private static void ShowInBox(TextBox box, string text)
    {
        if (box.OriginalText != text)
        {
            box.SetText(text);
        }
    }

    private static string BuildReadout(ReadoutKey key, DebugLiveSection section)
    {
        var frozen = key.IsFrozen ? "Frozen (Space): " : string.Empty;
        if (section._timeline.Selection is null)
        {
            return $"{frozen}Click a frame, or drag across frames, to read them; right-click clears.";
        }

        if (!section._timeline.TryGetSelectionSummary(out var summary))
        {
            return $"{frozen}The selected frames are outside the window; right-click clears.";
        }

        var milliseconds = GaugeUnit.Milliseconds;
        var readout = summary.FrameCount == 1
            ? $"{frozen}Frame {summary.FirstFrame:N0}: {GaugeValueFormatting.Format(milliseconds, summary.MaximumMilliseconds)}"
            : $"{frozen}Frames {summary.FirstFrame:N0}-{summary.LastFrame:N0} ({summary.FrameCount:N0}): avg {GaugeValueFormatting.Format(milliseconds, summary.AverageMilliseconds)}, max {GaugeValueFormatting.Format(milliseconds, summary.MaximumMilliseconds)} at {summary.SlowestFrame:N0}";
        var gauges = section._shownGauges ?? [];
        var selected = section._gaugeList.SelectedGaugeIndexes;
        for (var bandIndex = 0; bandIndex < selected.Count; bandIndex++)
        {
            if (section._timeline.TryGetSelectedGaugeSummary(bandIndex, out var average, out var maximum))
            {
                var gauge = gauges[selected[bandIndex]];
                readout += summary.FrameCount == 1
                    ? $"   {gauge.Name} {GaugeValueFormatting.Format(gauge.Unit, maximum)}"
                    : $"   {gauge.Name} avg {GaugeValueFormatting.Format(gauge.Unit, average)}";
            }
        }

        return readout;
    }

    /// <summary>Space: freezes or unfreezes the window, while the Live section is shown.</summary>
    private void ToggleFreezeWhileShown()
    {
        if (!Window.IsVisible || !ShownViews.Contains(LiveView.Gauges))
        {
            return;
        }

        _freeze.SetIsOn(!_freeze.IsOn);
        OnFreezeToggled(_freeze.IsOn);
    }

    /// <summary>What the header shows, at the precision it shows it: the frame by quarter second, rates as whole numbers.</summary>
    private readonly record struct HeaderKey(LiveDiagnosticsSession? Session, DiagnosticsConnectionState State, DiagnosticsLinkHello? Hello, long LatestFrame, int UpdateRate, int DrawRate, bool IsPaused, long DroppedFrameCount)
    {
        public static HeaderKey Of(LiveDiagnosticsSession? session) => session is null
            ? default
            : new HeaderKey(session, session.State, session.Hello, LatestFrameShown(session), (int)System.Math.Round(session.UpdateRate), (int)System.Math.Round(session.DrawRate), session.IsPaused, session.DroppedFrameCount);

        private static long LatestFrameShown(LiveDiagnosticsSession session) => session.LatestFrame / DebugTabChrome.TextRefreshFrames * DebugTabChrome.TextRefreshFrames;
    }

    private readonly record struct ReadoutKey(int FrameSelectionVersion, int GaugeSelectionVersion, long EndBucket, bool IsFrozen);

    private readonly record struct RefreshKey(DiagnosticsFrameHistory? History, long EndBucket, int WindowFrames, int SelectionVersion);

    /// <summary>A press on the timeline selects a frame, dragging selects a range until the release, and a right-click on it clears the selection.</summary>
    private sealed class TimelineSelectionInputHandler(DiagnosticsTimelineView timeline) : IUiInputHandler
    {
        private bool _isSelecting;

        public void OnPress(Element? pressedElement, Point position)
        {
            if (ReferenceEquals(pressedElement, timeline))
            {
                timeline.BeginSelection(position);
                _isSelecting = true;
            }
        }

        public void OnPointerUpdate(Element? hoveredElement, Point position, bool isInteractionInProgress)
        {
            if (_isSelecting)
            {
                timeline.ExtendSelection(position);
            }
        }

        public void OnRelease(Element? releasedElement, Point position, bool isTap)
        {
            if (!_isSelecting)
            {
                return;
            }

            timeline.ExtendSelection(position);
            timeline.EndSelection();
            _isSelecting = false;
        }

        public void OnRightPress(Element? pressedElement, Point position)
        {
            if (ReferenceEquals(pressedElement, timeline))
            {
                timeline.ClearSelection();
            }
        }
    }
}
