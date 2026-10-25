using Engine.Diagnostics;
using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace Workshop.Debug;

/// <summary>Every gauge of a connected game, grouped by gauge group: its latest value, minimum, maximum and average over the window shown, and a sparkline of that window. A click selects a row or unselects it.</summary>
/// <remarks>
/// Drawn by itself rather than from child elements, so only the rows on screen cost anything. The owner hands it the rows'
/// text and sparkline values (SetWindow) whenever it rebuilds them; nothing here formats per frame. With no gauges it
/// shows Message instead -- why there are none.
/// </remarks>
public sealed class DiagnosticsGaugeList(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
    : Element(fontService, elementPoolService, renderService)
{
    private static readonly string[] ColumnHeadings = ["Gauge", "Value", "Min", "Max", "Avg"];
    private static readonly float[] ColumnX = [DebugTabChrome.GaugeNameIndent, DebugTabChrome.GaugeValueColumnX, DebugTabChrome.GaugeMinimumColumnX, DebugTabChrome.GaugeMaximumColumnX, DebugTabChrome.GaugeAverageColumnX];

    private readonly SpriteFontBase _font = fontService.GetFont(FontChrome.DefaultFontSize);
    private readonly List<int> _selectedGaugeIndexes = [];

    /// <summary>A row: a group's heading (GaugeIndex -1, its name in GroupName) or a gauge.</summary>
    private readonly List<(int GaugeIndex, string GroupName)> _rows = [];

    private IReadOnlyList<DiagnosticsLinkGaugeDescriptor> _gauges = [];
    private IReadOnlyList<GaugeRowText> _rowTexts = [];
    private double[][] _sparklineValues = [];
    private int _sparklineValueCount;

    /// <summary>How many points each row's sparkline draws -- one pixel column each.</summary>
    public int SparklinePointCount => _sparklineValueCount;

    /// <summary>Shown in place of the rows when there are no gauges.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>The selected gauges, in the order they were selected.</summary>
    public IReadOnlyList<int> SelectedGaugeIndexes => _selectedGaugeIndexes;

    public event Action? SelectionChanged;

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        _selectedGaugeIndexes.Clear();
        _rows.Clear();
        _gauges = [];
        _rowTexts = [];
        _sparklineValues = [];
        _sparklineValueCount = 0;
        Message = string.Empty;
        CanUserScrollVertical = true;
        SetContentColor(options.Content?.ContentColor ?? Color.White);
    }

    /// <summary>Lists gauges, grouped by their group in the order each group first appears, and clears the selection.</summary>
    public void SetGauges(IReadOnlyList<DiagnosticsLinkGaugeDescriptor> gauges)
    {
        _gauges = gauges;
        _rowTexts = [];
        _sparklineValueCount = 0;
        _selectedGaugeIndexes.Clear();
        _rows.Clear();
        foreach (var groupName in gauges.Select(gauge => gauge.Group).Distinct())
        {
            _rows.Add((-1, groupName));
            for (var gaugeIndex = 0; gaugeIndex < gauges.Count; gaugeIndex++)
            {
                if (gauges[gaugeIndex].Group == groupName)
                {
                    _rows.Add((gaugeIndex, groupName));
                }
            }
        }

        UpdateScrollBounds();
        SettleScrollbarsOutsideMeasure();
        SelectionChanged?.Invoke();
    }

    /// <summary>The rows' text, one per gauge, and each gauge's values over the window, valueCount of each, oldest first.</summary>
    public void SetWindow(IReadOnlyList<GaugeRowText> rowTexts, double[][] sparklineValues, int valueCount)
    {
        _rowTexts = rowTexts;
        _sparklineValues = sparklineValues;
        _sparklineValueCount = valueCount;
    }

    /// <summary>Selects gaugeIndex, or unselects it when it is selected.</summary>
    public void ToggleSelection(int gaugeIndex)
    {
        if (!_selectedGaugeIndexes.Remove(gaugeIndex))
        {
            _selectedGaugeIndexes.Add(gaugeIndex);
        }

        SelectionChanged?.Invoke();
    }

    /// <summary>The content-local top of gaugeIndex's row; -1 when it isn't listed.</summary>
    public float RowTopOf(int gaugeIndex)
    {
        var rowIndex = _rows.FindIndex(row => row.GaugeIndex == gaugeIndex);
        return rowIndex < 0 ? -1 : DebugTabChrome.GaugeListRowHeight * (rowIndex + 1);
    }

    protected override void RecalculateFixedSize()
    {
        base.RecalculateFixedSize();
        UpdateScrollBounds();
    }

    protected override void OnContentClickAction(Point mousePosition)
    {
        base.OnContentClickAction(mousePosition);

        var localY = mousePosition.Y - ContentAbsolutePosition.Y;
        if (localY < DebugTabChrome.GaugeListRowHeight)
        {
            return;
        }

        var rowIndex = (int)((localY + ScrollOffset.Y) / DebugTabChrome.GaugeListRowHeight) - 1;
        if (rowIndex >= 0 && rowIndex < _rows.Count && _rows[rowIndex].GaugeIndex >= 0)
        {
            ToggleSelection(_rows[rowIndex].GaugeIndex);
        }
    }

    public override void DrawContent(GameTime gameTime)
    {
        var batch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;
        var width = ContentSize.X;
        var rowHeight = DebugTabChrome.GaugeListRowHeight;
        if (_gauges.Count == 0)
        {
            LabelRenderer.DrawLeftAligned(batch, _font, Message, new Vector2(4, 4), new Vector2(width - 8, rowHeight), DebugTabChrome.WarningText);
            return;
        }

        var firstVisible = System.Math.Max(0, (int)(ScrollOffset.Y / rowHeight));
        var lastVisible = System.Math.Min(_rows.Count - 1, (int)((ScrollOffset.Y + ContentSize.Y) / rowHeight));
        for (var rowIndex = firstVisible; rowIndex <= lastVisible; rowIndex++)
        {
            var top = rowHeight * (rowIndex + 1);
            var (gaugeIndex, groupName) = _rows[rowIndex];
            if (gaugeIndex < 0)
            {
                LabelRenderer.DrawLeftAligned(batch, _font, groupName, new Vector2(4, top), new Vector2(width - 8, rowHeight), DebugTabChrome.GroupHeading);
                continue;
            }

            if (_selectedGaugeIndexes.Contains(gaugeIndex))
            {
                batch.Draw(unitRectangle, new Rectangle(0, (int)top, (int)width, (int)rowHeight), DebugTabChrome.SelectedRow);
            }

            LabelRenderer.DrawLeftAligned(batch, _font, _gauges[gaugeIndex].Name, new Vector2(ColumnX[0], top), new Vector2(ColumnX[1] - ColumnX[0], rowHeight), WindowPalette.BodyTextColor);
            if (gaugeIndex < _rowTexts.Count)
            {
                var text = _rowTexts[gaugeIndex];
                DrawCell(text.Value, 1, top);
                DrawCell(text.Minimum, 2, top);
                DrawCell(text.Maximum, 3, top);
                DrawCell(text.Average, 4, top);
            }

            var sparklineWidth = System.Math.Min(DebugTabChrome.GaugeSparklineWidth, width - DebugTabChrome.GaugeSparklineColumnX - 4);
            if (sparklineWidth > 8 && gaugeIndex < _sparklineValues.Length)
            {
                DrawSparkline(gaugeIndex, new Rectangle((int)DebugTabChrome.GaugeSparklineColumnX, (int)(top + DebugTabChrome.GaugeSparklineVerticalGap), (int)sparklineWidth, (int)(rowHeight - DebugTabChrome.GaugeSparklineVerticalGap * 2)));
            }
        }

        DrawColumnHeadings(width);
    }

    private void DrawCell(string text, int columnIndex, float top)
    {
        var columnWidth = (columnIndex + 1 < ColumnX.Length ? ColumnX[columnIndex + 1] : DebugTabChrome.GaugeSparklineColumnX) - ColumnX[columnIndex];
        LabelRenderer.DrawLeftAligned(Render.Batch, _font, text, new Vector2(ColumnX[columnIndex], top), new Vector2(columnWidth, DebugTabChrome.GaugeListRowHeight), WindowPalette.BodyTextColor);
    }

    private void DrawSparkline(int gaugeIndex, Rectangle area)
    {
        var values = _sparklineValues[gaugeIndex].AsSpan(0, _sparklineValueCount);
        var statistics = GaugeWindowStatistics.Of(values);
        if (_gauges[gaugeIndex].Kind is GaugeKind.Cumulative)
        {
            SparklineRenderer.Draw(Render.Batch, Render.UnitRectangle, area, values, 0, System.Math.Max(statistics.Maximum, 1), SparklineStyle.Bars, DebugTabChrome.CumulativeSeries, DebugTabChrome.GraphBackground);
            return;
        }

        var padding = statistics.Maximum == statistics.Minimum ? System.Math.Max(1, System.Math.Abs(statistics.Maximum) * 0.01) : 0;
        SparklineRenderer.Draw(Render.Batch, Render.UnitRectangle, area, values, statistics.Minimum - padding, statistics.Maximum + padding, SparklineStyle.Line, DebugTabChrome.LevelSeries, DebugTabChrome.GraphBackground);
    }

    /// <summary>Drawn last, over any row scrolled up beneath it, at the top of what is shown.</summary>
    private void DrawColumnHeadings(float width)
    {
        var top = ScrollOffset.Y;
        Render.Batch.Draw(Render.UnitRectangle, new Rectangle(0, (int)top, (int)width, (int)DebugTabChrome.GaugeListRowHeight), WindowPalette.ControlBackground);
        for (var columnIndex = 0; columnIndex < ColumnHeadings.Length; columnIndex++)
        {
            LabelRenderer.DrawLeftAligned(Render.Batch, _font, ColumnHeadings[columnIndex], new Vector2(ColumnX[columnIndex], top), new Vector2(90, DebugTabChrome.GaugeListRowHeight), WindowPalette.ControlLabelTextColor);
        }
    }

    private void UpdateScrollBounds() =>
        SetMaxScrollOffset(new Vector2(0, System.Math.Max(0, DebugTabChrome.GaugeListRowHeight * (_rows.Count + 1) - ContentSize.Y)));
}
