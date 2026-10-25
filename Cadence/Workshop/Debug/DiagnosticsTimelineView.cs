using Engine.Diagnostics;
using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.Chrome;

namespace Workshop.Debug;

/// <summary>A window of a connected game's frames on one time axis: each frame's update milliseconds as bars, spikes marked, then a band per selected gauge, and the frames selected on it.</summary>
/// <remarks>
/// The window is a fixed span of records ending at the record its owner names, so the axis doesn't stretch while the history
/// fills. A pixel column covering several frames draws their largest, so a one-frame spike is never averaged away. A click
/// selects the column's slowest frame; a drag selects every frame from the column it started in to the one it ends on
/// (BeginSelection, ExtendSelection, EndSelection, driven by the tab's input handler). The selection is held as records, so it
/// moves with its frames while the window advances. Everything it draws is copied out of the history by Show, once a frame,
/// into buffers sized for the longest window.
/// </remarks>
public sealed class DiagnosticsTimelineView(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
    : Element(fontService, elementPoolService, renderService)
{
    private readonly SpriteFontBase _font = fontService.GetFont(FontChrome.DefaultFontSize);
    private double[] _updateMilliseconds = [];
    private double[][] _gaugeValues = [];
    private long[] _frames = [];
    private int _windowFrames;
    private long _windowStartRecord;
    private int _copiedOffset;
    private int _copiedCount;
    private IReadOnlyList<DiagnosticsLinkGaugeDescriptor> _gauges = [];
    private IReadOnlyList<int> _bandGauges = [];
    private IReadOnlyList<string> _bandLabels = [];
    private string _frameLabel = string.Empty;

    /// <summary>The column a press started a selection in, and its held records; null when no selection is being made.</summary>
    private (int Column, long FirstRecord, long LastRecord)? _selectionAnchor;
    private bool _hasLeftAnchorColumn;

    /// <summary>The height the timeline needs with bandCount gauge bands under its frame band.</summary>
    public static float HeightFor(int bandCount) =>
        DebugTabChrome.FrameBandHeight + bandCount * (DebugTabChrome.GaugeBandHeight + DebugTabChrome.BandGap);

    /// <summary>The selected frames, first and last record inclusive; null when nothing is selected.</summary>
    public (long FirstRecord, long LastRecord)? Selection { get; private set; }

    /// <summary>Changes every time Selection does.</summary>
    public int SelectionVersion { get; private set; }

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        _windowFrames = 0;
        _copiedCount = 0;
        _bandGauges = [];
        _bandLabels = [];
        _frameLabel = string.Empty;
        _selectionAnchor = null;
        _hasLeftAnchorColumn = false;
        Selection = null;
    }

    /// <summary>Copies records [endRecord - windowFrames, endRecord) of history -- update milliseconds and bandGauges' values -- to draw this frame.</summary>
    /// <param name="bandLabels">One label per band gauge, drawn over its band.</param>
    /// <param name="frameLabel">Drawn over the frame band.</param>
    public void Show(DiagnosticsFrameHistory? history, IReadOnlyList<DiagnosticsLinkGaugeDescriptor> gauges, long endRecord, int windowFrames, IReadOnlyList<int> bandGauges, IReadOnlyList<string> bandLabels, string frameLabel)
    {
        _windowFrames = windowFrames;
        _windowStartRecord = endRecord - windowFrames;
        _gauges = gauges;
        _bandGauges = bandGauges;
        _bandLabels = bandLabels;
        _frameLabel = frameLabel;
        if (history is null)
        {
            _copiedCount = 0;
            return;
        }

        EnsureCapacity(windowFrames, bandGauges.Count);
        _copiedCount = history.CopyWindow(0, endRecord, windowFrames, _updateMilliseconds);
        history.CopyFrameWindow(endRecord, windowFrames, _frames);
        _copiedOffset = (int)(System.Math.Max(_windowStartRecord, history.OldestHeldRecord) - _windowStartRecord);
        for (var bandIndex = 0; bandIndex < bandGauges.Count; bandIndex++)
        {
            history.CopyWindow(bandGauges[bandIndex] + 1, endRecord, windowFrames, _gaugeValues[bandIndex]);
        }
    }

    /// <summary>A press at screenPosition: selects the slowest frame in the column under it, and anchors a drag there. Nothing when the column holds no frame.</summary>
    public void BeginSelection(Point screenPosition)
    {
        var column = ColumnAt(screenPosition);
        if (!TryGetColumnRecords(column, out var firstRecord, out var lastRecord))
        {
            _selectionAnchor = null;
            return;
        }

        _selectionAnchor = (column, firstRecord, lastRecord);
        _hasLeftAnchorColumn = false;
        var slowestRecord = SlowestRecordIn(firstRecord, lastRecord);
        SetSelection((slowestRecord, slowestRecord));
    }

    /// <summary>The pointer at screenPosition while the press is held: once it leaves the anchor's column, selects every frame from the anchor's column to its own.</summary>
    public void ExtendSelection(Point screenPosition)
    {
        if (_selectionAnchor is not { } anchor)
        {
            return;
        }

        var column = ColumnAt(screenPosition);
        if (column == anchor.Column && !_hasLeftAnchorColumn)
        {
            return;
        }

        _hasLeftAnchorColumn = true;
        if (!TryGetColumnRecords(column, out var firstRecord, out var lastRecord))
        {
            return;
        }

        SetSelection((System.Math.Min(anchor.FirstRecord, firstRecord), System.Math.Max(anchor.LastRecord, lastRecord)));
    }

    /// <summary>The press was released: the selection stays.</summary>
    public void EndSelection() => _selectionAnchor = null;

    public void ClearSelection()
    {
        _selectionAnchor = null;
        SetSelection(null);
    }

    /// <summary>The selected frames still in the window: the first and last frame numbers, how many, their average and largest update milliseconds and the slowest one's frame; false when none are in the window.</summary>
    public bool TryGetSelectionSummary(out FrameSelectionSummary summary)
    {
        summary = default;
        if (!TryGetSelectedIndexes(out var first, out var last))
        {
            return false;
        }

        var total = 0d;
        var slowest = first;
        for (var index = first; index <= last; index++)
        {
            total += _updateMilliseconds[index];
            if (_updateMilliseconds[index] > _updateMilliseconds[slowest])
            {
                slowest = index;
            }
        }

        var count = last - first + 1;
        summary = new FrameSelectionSummary(_frames[first], _frames[last], count, total / count, _updateMilliseconds[slowest], _frames[slowest]);
        return true;
    }

    /// <summary>Band gauge bandIndex over the selected frames still in the window: its average and largest value; false when none are.</summary>
    public bool TryGetSelectedGaugeSummary(int bandIndex, out double average, out double maximum)
    {
        average = 0;
        maximum = 0;
        if (bandIndex >= _bandGauges.Count || !TryGetSelectedIndexes(out var first, out var last))
        {
            return false;
        }

        var statistics = GaugeWindowStatistics.Of(_gaugeValues[bandIndex].AsSpan(first, last - first + 1));
        average = statistics.Average;
        maximum = statistics.Maximum;
        return true;
    }

    public override void DrawContent(GameTime gameTime)
    {
        var batch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;
        var width = (int)ContentSize.X;
        var frameBand = new Rectangle(0, 0, width, (int)DebugTabChrome.FrameBandHeight);
        batch.Draw(unitRectangle, frameBand, DebugTabChrome.GraphBackground);

        var frameMaximum = System.Math.Max(MaximumOf(_updateMilliseconds), DebugTabChrome.FrameBudgetMilliseconds * 1.25);
        for (var column = 0; column < width; column++)
        {
            if (!TryGetColumnRange(column, width, out var first, out var end))
            {
                continue;
            }

            var value = _updateMilliseconds[first];
            for (var index = first + 1; index < end; index++)
            {
                value = System.Math.Max(value, _updateMilliseconds[index]);
            }

            var top = YOf(frameBand, value, 0, frameMaximum);
            var color = value > DebugTabChrome.FrameBudgetMilliseconds ? DebugTabChrome.SpikeSeries : DebugTabChrome.FrameSeries;
            batch.Draw(unitRectangle, new Rectangle(column, top, 1, frameBand.Bottom - top), color);
        }

        batch.Draw(unitRectangle, new Rectangle(0, YOf(frameBand, DebugTabChrome.FrameBudgetMilliseconds, 0, frameMaximum), width, 1), DebugTabChrome.FrameBudgetLine);
        LabelRenderer.Draw(batch, _font, _frameLabel, new Vector2(4, 2), DebugTabChrome.GraphLabel);

        var bandTop = DebugTabChrome.FrameBandHeight + DebugTabChrome.BandGap;
        for (var bandIndex = 0; bandIndex < _bandGauges.Count; bandIndex++)
        {
            var band = new Rectangle(0, (int)bandTop, width, (int)DebugTabChrome.GaugeBandHeight);
            var isCumulative = _gauges[_bandGauges[bandIndex]].Kind is GaugeKind.Cumulative;
            DrawGaugeBand(band, _gaugeValues[bandIndex], isCumulative, bandIndex < _bandLabels.Count ? _bandLabels[bandIndex] : string.Empty);
            bandTop += DebugTabChrome.GaugeBandHeight + DebugTabChrome.BandGap;
        }

        DrawSelection(width);
    }

    /// <summary>A single frame as a line over its column; a range as a wash over its columns.</summary>
    private void DrawSelection(int width)
    {
        if (Selection is not { } selection || _windowFrames <= 0 || width <= 0)
        {
            return;
        }

        var firstColumn = (int)((selection.FirstRecord - _windowStartRecord) * width / _windowFrames);
        var lastColumn = (int)((selection.LastRecord - _windowStartRecord) * width / _windowFrames);
        if (lastColumn < 0 || firstColumn >= width)
        {
            return;
        }

        firstColumn = System.Math.Max(0, firstColumn);
        lastColumn = System.Math.Min(width - 1, lastColumn);
        var height = (int)ContentSize.Y;
        if (selection.FirstRecord == selection.LastRecord)
        {
            Render.Batch.Draw(Render.UnitRectangle, new Rectangle(firstColumn, 0, 1, height), DebugTabChrome.Cursor);
            return;
        }

        Render.Batch.Draw(Render.UnitRectangle, new Rectangle(firstColumn, 0, lastColumn - firstColumn + 1, height), DebugTabChrome.SelectionWash);
    }

    private void DrawGaugeBand(Rectangle band, double[] values, bool isCumulative, string label)
    {
        var batch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;
        batch.Draw(unitRectangle, band, DebugTabChrome.GraphBackground);
        var statistics = GaugeWindowStatistics.Of(values.AsSpan(0, _copiedCount));
        var padding = statistics.Maximum == statistics.Minimum ? System.Math.Max(1, System.Math.Abs(statistics.Maximum) * 0.01) : 0;
        var minimum = isCumulative ? 0 : statistics.Minimum - padding;
        var maximum = isCumulative ? System.Math.Max(statistics.Maximum, 1) : statistics.Maximum + padding;
        var previousY = -1;
        for (var column = 0; column < band.Width; column++)
        {
            if (!TryGetColumnRange(column, band.Width, out var first, out var end))
            {
                previousY = -1;
                continue;
            }

            var value = values[first];
            for (var index = first + 1; index < end; index++)
            {
                value = System.Math.Max(value, values[index]);
            }

            var y = YOf(band, value, minimum, maximum);
            if (isCumulative)
            {
                if (value > 0)
                {
                    batch.Draw(unitRectangle, new Rectangle(band.X + column, y, 1, band.Bottom - y), DebugTabChrome.CumulativeSeries);
                }

                continue;
            }

            var top = previousY < 0 ? y : System.Math.Min(previousY, y);
            var bottom = previousY < 0 ? y : System.Math.Max(previousY, y);
            batch.Draw(unitRectangle, new Rectangle(band.X + column, top, 1, bottom - top + 1), DebugTabChrome.LevelSeries);
            previousY = y;
        }

        LabelRenderer.Draw(batch, _font, label, new Vector2(band.X + 4, band.Y + 2), DebugTabChrome.GraphLabel);
    }

    /// <summary>The copied values column covers, as [first, end) indices into the buffers; false when it covers no held record.</summary>
    private bool TryGetColumnRange(int column, int width, out int first, out int end)
    {
        first = 0;
        end = 0;
        if (_windowFrames <= 0 || width <= 0 || _copiedCount == 0)
        {
            return false;
        }

        var columnStart = (int)((long)column * _windowFrames / width);
        var columnEnd = System.Math.Max(columnStart + 1, (int)((long)(column + 1) * _windowFrames / width));
        first = System.Math.Max(columnStart, _copiedOffset) - _copiedOffset;
        end = System.Math.Min(columnEnd, _copiedOffset + _copiedCount) - _copiedOffset;
        return end > first;
    }

    /// <summary>The column under screenPosition, kept within the timeline, so a drag past either edge selects to that edge.</summary>
    private int ColumnAt(Point screenPosition) =>
        System.Math.Clamp((int)(screenPosition.X - ContentAbsolutePosition.X), 0, System.Math.Max(0, (int)ContentSize.X - 1));

    /// <summary>The first and last held records column covers; false when it covers none.</summary>
    private bool TryGetColumnRecords(int column, out long firstRecord, out long lastRecord)
    {
        firstRecord = 0;
        lastRecord = 0;
        if (!TryGetColumnRange(column, (int)ContentSize.X, out var first, out var end))
        {
            return false;
        }

        firstRecord = RecordOf(first);
        lastRecord = RecordOf(end - 1);
        return true;
    }

    private long SlowestRecordIn(long firstRecord, long lastRecord)
    {
        var slowest = IndexOf(firstRecord);
        for (var index = slowest + 1; index <= IndexOf(lastRecord); index++)
        {
            if (_updateMilliseconds[index] > _updateMilliseconds[slowest])
            {
                slowest = index;
            }
        }

        return RecordOf(slowest);
    }

    /// <summary>The buffer indexes of the selected records still in the window; false when none are.</summary>
    private bool TryGetSelectedIndexes(out int first, out int last)
    {
        first = 0;
        last = 0;
        if (Selection is not { } selection || _copiedCount == 0)
        {
            return false;
        }

        first = (int)System.Math.Max(IndexOf(selection.FirstRecord), 0);
        last = (int)System.Math.Min(IndexOf(selection.LastRecord), _copiedCount - 1);
        return last >= first;
    }

    private long RecordOf(int index) => _windowStartRecord + _copiedOffset + index;

    private int IndexOf(long record) => (int)System.Math.Clamp(record - _windowStartRecord - _copiedOffset, int.MinValue, int.MaxValue);

    private void SetSelection((long FirstRecord, long LastRecord)? selection)
    {
        if (selection == Selection)
        {
            return;
        }

        Selection = selection;
        SelectionVersion++;
    }

    private double MaximumOf(double[] values)
    {
        var maximum = 0d;
        for (var index = 0; index < _copiedCount; index++)
        {
            maximum = System.Math.Max(maximum, values[index]);
        }

        return maximum;
    }

    private void EnsureCapacity(int windowFrames, int bandCount)
    {
        if (_updateMilliseconds.Length < windowFrames)
        {
            _updateMilliseconds = new double[windowFrames];
            _frames = new long[windowFrames];
            _gaugeValues = [];
        }

        if (_gaugeValues.Length < bandCount)
        {
            var grown = new double[bandCount][];
            for (var bandIndex = 0; bandIndex < bandCount; bandIndex++)
            {
                grown[bandIndex] = bandIndex < _gaugeValues.Length ? _gaugeValues[bandIndex] : new double[_updateMilliseconds.Length];
            }

            _gaugeValues = grown;
        }
    }

    private static int YOf(Rectangle area, double value, double minimum, double maximum)
    {
        var fraction = System.Math.Clamp((value - minimum) / System.Math.Max(maximum - minimum, double.Epsilon), 0, 1);
        return System.Math.Min(area.Bottom - 1, area.Bottom - 1 - (int)System.Math.Round(fraction * (area.Height - 1)));
    }
}
