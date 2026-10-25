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

/// <summary>The Debug tab's Live section: the connected game's state, its frame graph with the selected gauges under it, and every gauge with its window's statistics.</summary>
/// <remarks>
/// <para>
/// The graphs show a window of the session's history -- the last 5 s, 30 s, 2 min or 10 min -- ending at its newest frame, or
/// at the frame Freeze stopped at (Freeze or Space; the session keeps recording). A click on the timeline selects a frame and a
/// drag a range, read out under the window choice; a right-click clears it. Selecting a gauge's row draws it large under the frame graph, on the same axis. The game's pid and seed sit in read-only boxes, to copy.
/// </para>
/// <para>
/// Under the graphs a switch picks what fills the rest: the gauges, or one of the latest reports' views -- frame costs,
/// memory, leaks, startup (LiveReportViews) -- with a line over it saying what it shows, or why it is empty.
/// </para>
/// <para>
/// Text is rebuilt only when what it shows changes, at the precision shown: the header and the gauge rows every
/// DebugTabChrome.TextRefreshFrames records (or when the window, selection or session changes), the readout when the selection
/// does. The graphs redraw every frame from the history.
/// </para>
/// </remarks>
public sealed class DebugLiveSection(DebugTabServices services, string title = "Live", string idleText = DebugLiveSection.WaitingForPlayText)
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
    private TextWindow _viewMessage = null!;
    private Rectangle _viewBounds;

    private readonly KeyedCache<HeaderKey, string> _connectionText = new();
    private readonly KeyedCache<HeaderKey, string> _headerText = new();
    private readonly KeyedCache<ReadoutKey, string> _readoutText = new();
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

    /// <summary>What shows under the graphs.</summary>
    public LiveView View { get; private set; } = LiveView.Gauges;

    public LiveReportViews ReportViews => _reportViews;

    /// <summary>view's tile on the switch under the graphs.</summary>
    public TextWindow TileFor(LiveView view) => _viewTiles.First(entry => entry.View == view).Tile;

    /// <summary>The line over the view, as shown.</summary>
    public string ViewMessageText => _viewMessage.OriginalText;

    /// <summary>The game's process id as shown, in its read-only box.</summary>
    public TextBox ProcessIdBox => _processIdBox;

    /// <summary>The game's seed as shown, in its read-only box.</summary>
    public TextBox SeedBox => _seedBox;

    /// <summary>The connection line beside them, as shown.</summary>
    public string ConnectionText => _connection.OriginalText;

    /// <summary>The header as shown.</summary>
    public string HeaderText => _header.OriginalText;

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
        foreach (var (view, label) in Views)
        {
            _viewTiles.Add((view, DebugTabElements.CreateTile(services, Window, Vector2.Zero, ViewTileWidth, label, () => ShowView(view))));
        }

        _viewMessage = DebugTabElements.CreateText(services, Window, Vector2.Zero, Vector2.One, string.Empty);
        _reportViews.Create(Window);
        services.AddInputHandler(new TimelineSelectionInputHandler(_timeline));
        services.AddInputHandler(new KeyPressInputHandler(Microsoft.Xna.Framework.Input.Keys.Space, ToggleFreezeWhileShown));
        LayOut();
        ShowView(View);
    }

    public void ShowView(LiveView view)
    {
        View = view;
        foreach (var (tileView, tile) in _viewTiles)
        {
            DebugTabElements.StyleTile(tile, isSelected: tileView == view);
        }

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

    /// <summary>The view's line and its content -- the gauge list or a report's rows -- in the area under the switch.</summary>
    private void ShowViewContent()
    {
        var showsGauges = View == LiveView.Gauges;
        _gaugeList.IsVisible = showsGauges;
        _reportViews.Show(View, _viewBounds);
        _viewMessage.UpdateText(showsGauges
            ? _gaugeList.SelectedGaugeIndexes.Count == 0 && (_shownGauges?.Count ?? 0) > 0 ? GaugesHint : string.Empty
            : _shownSession is null ? _idleText : LiveReportViews.MessageFor(View, _shownSession));
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

        _windowChoice.SetRelativePosition(new Vector2(0, y));
        _freeze.SetRelativePosition(new Vector2(WindowChoiceWidth + DebugTabChrome.Margin, y));
        var readoutLeft = WindowChoiceWidth + FreezeWidth + DebugTabChrome.Margin * 2;
        DebugTabElements.Place(_readout, new Rectangle((int)readoutLeft, (int)y, (int)System.Math.Max(1, width - readoutLeft), (int)DebugTabChrome.RowHeight));
        y += DebugTabChrome.RowHeight + DebugTabChrome.RowGap;

        var viewHeaderHeight = DebugTabChrome.RowHeight * 2 + DebugTabChrome.RowGap;
        var timelineHeight = System.Math.Min(DiagnosticsTimelineView.HeightFor(_gaugeList.SelectedGaugeIndexes.Count), System.Math.Max(DebugTabChrome.FrameBandHeight, height - y - viewHeaderHeight - MinimumGaugeListHeight - DebugTabChrome.RowGap));
        DebugTabElements.Place(_timeline, new Rectangle(0, (int)y, (int)width, (int)timelineHeight));
        y += timelineHeight + DebugTabChrome.RowGap;

        x = 0f;
        foreach (var (_, tile) in _viewTiles)
        {
            tile.SetRelativePosition(new Vector2(x, y));
            x += ViewTileWidth;
        }

        y += DebugTabChrome.RowHeight + DebugTabChrome.RowGap;
        DebugTabElements.Place(_viewMessage, new Rectangle(0, (int)y, (int)width, (int)DebugTabChrome.RowHeight));
        y += DebugTabChrome.RowHeight;
        _viewBounds = new Rectangle(0, (int)y, (int)width, (int)System.Math.Max(DebugTabChrome.GaugeListRowHeight * 2, height - y));
        DebugTabElements.Place(_gaugeList, _viewBounds);
        _reportViews.Show(View, _viewBounds);
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
            return session.Source is { } source ? $"{Path.GetFileName(source)} holds no gauges." : "Gauges is off for this run: tick Gauges under Launch Settings, then Play again.";
        }

        return session.State == DiagnosticsConnectionState.Ended ? "The game ended before its session started." : "Waiting for the game's session to start.";
    }

    private static DiagnosticsFeatures? FeaturesOf(LiveDiagnosticsSession session) =>
        session.Hello is { } hello && Enum.TryParse<DiagnosticsFeatures>(hello.Features, out var features) ? features : null;

    /// <summary>The line beside the pid and seed: whether the game is connected, and frames dropped.</summary>
    private static string BuildConnection(HeaderKey key, DebugLiveSection section)
    {
        if (section._shownSession is not { } session)
        {
            return section._idleText;
        }

        if (session.Source is { } source)
        {
            return $"From {Path.GetFileName(source)}";
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
        if (!Window.IsVisible)
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

        public void OnRelease(Element? releasedElement, Point position)
        {
            if (!_isSelecting)
            {
                return;
            }

            timeline.ExtendSelection(position);
            timeline.EndSelection();
            _isSelecting = false;
        }

        public void OnRightPress(Point position)
        {
            if (timeline.IsVisible && timeline.Rectangle.Contains(position))
            {
                timeline.ClearSelection();
            }
        }
    }
}
