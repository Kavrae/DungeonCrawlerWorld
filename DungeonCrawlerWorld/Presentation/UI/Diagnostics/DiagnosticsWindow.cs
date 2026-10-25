using Engine.Diagnostics;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using FontStashSharp;
using Game.Modules.Movement.Components;
using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Diagnostics;

/// <summary>Live diagnostics: update and draw rates, entity counts, the frame-time graph, every gauge with its recent history, and the leak detector's findings.</summary>
/// <remarks>
/// Gauges come from DiagnosticsEngine.LiveGauges, which exists only while the Gauges feature is on;
/// without it the window shows the rates and counts alone. Its text is re-formatted every
/// DiagnosticsWindowChrome.TextRefreshIntervalSeconds rather than every frame, so the window adds
/// little to the allocation it is showing; the graphs redraw every frame from the history itself.
/// Drawn in content-local coordinates (the window scrolls), and rows outside the visible band are
/// skipped.
/// </remarks>
public sealed class DiagnosticsWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    UiRenderService renderService,
    EntityManager entityManager,
    PackedComponentPool<MovementComponent> movementPool,
    SimulationClock simulationClock,
    DiagnosticsEngine? diagnostics) : Window(fontService, elementPoolService, renderService)
{
    private const string GaugesOffHint = "Run with --diagnostics=gauges for live gauges.";
    private const string LeakDetectionHeading = "Leak findings";

    private readonly double[] _historyScratch = new double[GaugeHistory.HistoryFrameCount];
    private readonly List<string> _headerLines = [];
    private readonly List<string> _leakLines = [];

    private PerformanceCounter _updateCounter = new();
    private PerformanceCounter _drawCounter = new();
    private SpriteFontBase _font = null!;
    private string[] _rowValueTexts = [];
    private string[] _rowRangeTexts = [];
    private string _frameGraphLabel = string.Empty;
    private double _frameGraphMaximum;
    private bool _isRunningSlowly;
    private bool _isSimulationPaused;
    private long _lastSeenSimulationFrame = -1;
    private double _secondsSinceTextRefresh = double.MaxValue;
    private float _contentHeight;
    private float _layoutCursorY;
    private float _layoutVisibleTop;
    private float _layoutVisibleBottom;
    private bool _layoutIsDrawing;

    /// <summary>The height of everything the window lays out, as of its last text refresh; what its scroll range is measured against.</summary>
    public float ContentHeight => _contentHeight;

    private float RowHeight => System.Math.Max(DiagnosticsWindowChrome.RowHeight, _font.LineHeight);

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        _font = FontService.GetFont(GameFontChrome.DiagnosticsWindowFontSize);
        _updateCounter = new PerformanceCounter();
        _drawCounter = new PerformanceCounter();
        _headerLines.Clear();
        _leakLines.Clear();
        _rowValueTexts = [];
        _rowRangeTexts = [];
        _frameGraphLabel = string.Empty;
        _lastSeenSimulationFrame = -1;
        _secondsSinceTextRefresh = double.MaxValue;
        _contentHeight = 0;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        _updateCounter.Tick();
        _isRunningSlowly = gameTime.IsRunningSlowly;
        _isSimulationPaused = simulationClock.CurrentFrame == _lastSeenSimulationFrame;
        _lastSeenSimulationFrame = simulationClock.CurrentFrame;

        _secondsSinceTextRefresh += gameTime.ElapsedGameTime.TotalSeconds;
        if (_secondsSinceTextRefresh < DiagnosticsWindowChrome.TextRefreshIntervalSeconds)
        {
            return;
        }

        _secondsSinceTextRefresh = 0;
        RefreshText();

        var contentHeight = LayOut(isDrawing: false);
        if (contentHeight != _contentHeight)
        {
            _contentHeight = contentHeight;
            UpdateScrollBounds();
            SettleScrollbarsOutsideMeasure();
        }
    }

    protected override void RecalculateFixedSize()
    {
        base.RecalculateFixedSize();
        UpdateScrollBounds();
    }

    private void UpdateScrollBounds() => SetMaxScrollOffset(new Vector2(0, System.Math.Max(0, _contentHeight - ContentSize.Y)));

    public override void DrawContent(GameTime gameTime)
    {
        _drawCounter.Tick();
        LayOut(isDrawing: true);
    }

    private void RefreshText()
    {
        _headerLines.Clear();
        var pausedSuffix = _isSimulationPaused ? "   Paused" : string.Empty;
        _headerLines.Add($"{_updateCounter.RatePerSecond:N1} ups   {_drawCounter.RatePerSecond:N1} fps   frame {simulationClock.CurrentFrame:N0}{pausedSuffix}");
        _headerLines.Add($"Entities {entityManager.LivingEntityCount:N0}   Moving {movementPool.Count:N0}");
        if (diagnostics?.TopFrameCostEntry is { } topEntry)
        {
            _headerLines.Add($"Top: {topEntry.Name} {topEntry.MillisecondsPerSecond:N0} ms/s");
        }

        _headerLines.Add($"Seed {diagnostics?.RandomSeed?.ToString() ?? "unknown"}   Diagnostics: {diagnostics?.Features ?? DiagnosticsFeatures.None}");

        _leakLines.Clear();
        if (diagnostics?.LeakFindings is { } leakFindings)
        {
            foreach (var finding in leakFindings)
            {
                _leakLines.Add($"{finding.Subject}: +{finding.GrowthRatio:P0} beyond its population");
            }

            if (_leakLines.Count == 0)
            {
                _leakLines.Add("None");
            }
        }

        if (diagnostics?.LiveGauges is not { } liveGauges)
        {
            return;
        }

        var rowCount = liveGauges.Rows.Count;
        if (_rowValueTexts.Length != rowCount)
        {
            _rowValueTexts = new string[rowCount];
            _rowRangeTexts = new string[rowCount];
        }

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = liveGauges.Rows[rowIndex];
            var history = _historyScratch.AsSpan(0, liveGauges.CopyHistory(rowIndex, _historyScratch));
            var unit = GaugeUnits.Of(row.GaugeName);
            _rowValueTexts[rowIndex] = GaugeValueFormatting.Format(unit, liveGauges.GetLatestValue(rowIndex));
            _rowRangeTexts[rowIndex] = GaugeValueFormatting.FormatSummary(row.Kind, unit, GaugeWindowStatistics.Of(history));
        }

        var frameHistory = _historyScratch.AsSpan(0, liveGauges.CopyHistory(0, _historyScratch));
        var frameMaximum = GaugeWindowStatistics.Of(frameHistory).Maximum;
        _frameGraphMaximum = System.Math.Max(frameMaximum, DiagnosticsWindowChrome.FrameBudgetMilliseconds * 1.25);
        _frameGraphLabel = $"Update {_rowValueTexts[0]}   max {GaugeValueFormatting.Format(GaugeUnit.Milliseconds, frameMaximum)}";
    }

    /// <summary>Walks every line of the window from the top, drawing it when isDrawing is set, and returns the height it all takes.</summary>
    private float LayOut(bool isDrawing)
    {
        var inset = DiagnosticsWindowChrome.RowInset;
        var width = ContentSize.X - inset * 2;
        var rowHeight = RowHeight;
        _layoutIsDrawing = isDrawing;
        _layoutVisibleTop = ScrollOffset.Y - rowHeight;
        _layoutVisibleBottom = ScrollOffset.Y + ContentSize.Y;
        _layoutCursorY = inset;

        for (var lineIndex = 0; lineIndex < _headerLines.Count; lineIndex++)
        {
            LayOutTextLine(_headerLines[lineIndex], lineIndex == 0 && _isRunningSlowly ? GameWindowPalette.DiagnosticsWarningText : GameWindowPalette.DiagnosticsText);
        }

        if (diagnostics?.LiveGauges is not { } liveGauges || _rowValueTexts.Length != liveGauges.Rows.Count)
        {
            if (diagnostics?.Features.HasFlag(DiagnosticsFeatures.Gauges) != true)
            {
                LayOutTextLine(GaugesOffHint, GameWindowPalette.DiagnosticsWarningText);
            }

            LayOutLeakLines();
            return _layoutCursorY + inset;
        }

        _layoutCursorY += DiagnosticsWindowChrome.GroupGap;
        LayOutTextLine(_frameGraphLabel, GameWindowPalette.DiagnosticsGroupHeading);
        var frameGraphHeight = DiagnosticsWindowChrome.FrameGraphHeight;
        if (IsInVisibleBand(_layoutCursorY, frameGraphHeight))
        {
            var frameHistory = _historyScratch.AsSpan(0, liveGauges.CopyHistory(0, _historyScratch));
            SparklineRenderer.Draw(Render.Batch, Render.UnitRectangle, new Rectangle((int)inset, (int)_layoutCursorY, (int)width, (int)frameGraphHeight), frameHistory, 0, _frameGraphMaximum, SparklineStyle.Bars, GameWindowPalette.DiagnosticsLevelSeries, GameWindowPalette.DiagnosticsGraphBackground, DiagnosticsWindowChrome.FrameBudgetMilliseconds, GameWindowPalette.DiagnosticsFrameBudgetLine);
        }

        _layoutCursorY += frameGraphHeight;

        string? currentGroupName = null;
        for (var rowIndex = 1; rowIndex < liveGauges.Rows.Count; rowIndex++)
        {
            var row = liveGauges.Rows[rowIndex];
            if (row.GroupName != currentGroupName)
            {
                currentGroupName = row.GroupName;
                _layoutCursorY += DiagnosticsWindowChrome.GroupGap;
                LayOutTextLine(currentGroupName, GameWindowPalette.DiagnosticsGroupHeading);
            }

            if (IsInVisibleBand(_layoutCursorY, rowHeight))
            {
                DrawGaugeRow(liveGauges, rowIndex, row, _layoutCursorY, inset, width, rowHeight);
            }

            _layoutCursorY += rowHeight;
        }

        LayOutLeakLines();
        return _layoutCursorY + inset;
    }

    private bool IsInVisibleBand(float top, float height) => _layoutIsDrawing && top + height >= _layoutVisibleTop && top <= _layoutVisibleBottom;

    private void LayOutTextLine(string text, Color color)
    {
        var rowHeight = RowHeight;
        if (IsInVisibleBand(_layoutCursorY, rowHeight))
        {
            LabelRenderer.Draw(Render.Batch, _font, text, new Vector2(DiagnosticsWindowChrome.RowInset, _layoutCursorY), color);
        }

        _layoutCursorY += rowHeight;
    }

    private void LayOutLeakLines()
    {
        if (_leakLines.Count == 0)
        {
            return;
        }

        _layoutCursorY += DiagnosticsWindowChrome.GroupGap;
        LayOutTextLine(LeakDetectionHeading, GameWindowPalette.DiagnosticsGroupHeading);
        foreach (var leakLine in _leakLines)
        {
            LayOutTextLine(leakLine, GameWindowPalette.DiagnosticsWarningText);
        }
    }

    private void DrawGaugeRow(GaugeHistory liveGauges, int rowIndex, Gauge row, float y, float inset, float width, float rowHeight)
    {
        var spriteBatch = Render.Batch;
        LabelRenderer.Draw(spriteBatch, _font, row.GaugeName, new Vector2(inset * 3, y), GameWindowPalette.DiagnosticsText);
        LabelRenderer.Draw(spriteBatch, _font, _rowValueTexts[rowIndex], new Vector2(inset + DiagnosticsWindowChrome.ValueColumnX, y), GameWindowPalette.DiagnosticsText);
        LabelRenderer.Draw(spriteBatch, _font, _rowRangeTexts[rowIndex], new Vector2(inset + DiagnosticsWindowChrome.RangeColumnX, y), GameWindowPalette.DiagnosticsText);

        var sparklineWidth = DiagnosticsWindowChrome.SparklineWidth;
        var verticalGap = DiagnosticsWindowChrome.SparklineVerticalGap;
        var sparklineArea = new Rectangle((int)(inset + width - sparklineWidth), (int)(y + verticalGap), (int)sparklineWidth, (int)(rowHeight - verticalGap * 2));
        var history = _historyScratch.AsSpan(0, liveGauges.CopyHistory(rowIndex, _historyScratch));
        var (minimum, maximum, _, _) = GaugeWindowStatistics.Of(history);
        if (row.Kind is GaugeKind.Cumulative)
        {
            SparklineRenderer.Draw(spriteBatch, Render.UnitRectangle, sparklineArea, history, 0, System.Math.Max(maximum, 1), SparklineStyle.Bars, GameWindowPalette.DiagnosticsEventSeries, GameWindowPalette.DiagnosticsGraphBackground);
        }
        else
        {
            var padding = maximum == minimum ? System.Math.Max(1, System.Math.Abs(maximum) * 0.01) : 0;
            SparklineRenderer.Draw(spriteBatch, Render.UnitRectangle, sparklineArea, history, minimum - padding, maximum + padding, SparklineStyle.Line, GameWindowPalette.DiagnosticsLevelSeries, GameWindowPalette.DiagnosticsGraphBackground);
        }
    }

}
