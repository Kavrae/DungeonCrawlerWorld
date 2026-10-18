using System.Globalization;
using Engine.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI;

/// <summary>A ListView column: its header, its width, and whether clicking the header sorts by it. The last column fills whatever width is left, never less than its own.</summary>
public sealed record ListViewColumn(string Header, float Width, bool Sortable = true);

/// <summary>A ListView row: one text per column, an optional icon glyph and its colour, and whatever the owner wants back when it is selected or activated.</summary>
public sealed record ListViewRow(IReadOnlyList<string> Cells, string? IconGlyph = null, Color? IconColor = null, object? Tag = null);

/// <summary>
/// Rows under column headers -- the Error List, Dependents, a record picker. Clicking a sortable header sorts by it, and
/// again reverses it; a click selects a row and a second click on it within the double-click window activates it, as do
/// Enter and a double-click. Focused, Up/Down/Home/End move the selection. The header stays put while the rows scroll.
/// </summary>
/// <remarks>
/// Drawn by itself rather than from child elements, so a list of thousands costs only the rows on screen. A cell too long
/// for its column ends in an ellipsis, cut once when the rows or the widths change, never per frame. Sorting compares two
/// cells as numbers when both read as one, and as text otherwise; rows that compare equal keep the order SetRows gave.
/// The selection is the row itself, so it survives a sort, and SetRows keeps it when the new rows still hold it.
/// </remarks>
public sealed class ListView(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer)
    : Element(fontService, elementPoolService, labelRenderer)
{
    public const float RowHeight = 20f;
    public const float HeaderHeight = 22f;
    public const float IconColumnWidth = 20f;

    private const float CellInset = 4f;

    private static readonly Color HeaderColor = WindowPalette.ControlBackground;
    private static readonly Color HeaderTextColor = WindowPalette.ControlLabelTextColor;
    private static readonly Color SelectedRowColor = WindowPalette.HighlightColor;
    private static readonly Color AlternateRowColor = Color.Black * 0.04f;

    private readonly SpriteFontBase _font = fontService.GetFont(FontChrome.DefaultFontSize);

    private readonly ITextMeasurer _textMeasurer = fontService.GetTextMeasurer(fontService.GetFont(FontChrome.DefaultFontSize));

    private IReadOnlyList<ListViewColumn> _columns = [];
    private bool _showIconColumn;
    private IReadOnlyList<ListViewRow> _rows = [];

    /// <summary>Indices into _rows in the order shown.</summary>
    private int[] _order = [];

    /// <summary>Each row's cells as drawn -- cut to its column's width -- indexed like _rows.</summary>
    private string[][] _displayCells = [];

    private string[] _displayHeaders = [];

    /// <summary>The headers as drawn: _displayHeaders with the sort column's direction marker.</summary>
    private string[] _drawnHeaders = [];
    private float[] _columnWidths = [];

    private int _sortColumnIndex = -1;
    private bool _sortAscending = true;

    private int _framesSinceLastRowClick = int.MaxValue;
    private ListViewRow? _lastClickedRow;

    public ListViewRow? SelectedRow { get; private set; }

    /// <summary>The column the rows are sorted by, or -1 for SetRows's order.</summary>
    public int SortColumnIndex => _sortColumnIndex;

    public bool SortAscending => _sortAscending;

    /// <summary>The rows in the order shown.</summary>
    public IEnumerable<ListViewRow> RowsInDisplayOrder => _order.Select(rowIndex => _rows[rowIndex]);

    /// <summary>Raised with the newly selected row, or null when the selection is cleared.</summary>
    public event Action<ListViewRow?>? SelectionChanged;

    /// <summary>Raised by a double-click on a row or Enter on the selected one.</summary>
    public event Action<ListViewRow>? RowActivated;

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        _columns = [];
        _showIconColumn = false;
        _rows = [];
        _order = [];
        _displayCells = [];
        _displayHeaders = [];
        _drawnHeaders = [];
        _columnWidths = [];
        _sortColumnIndex = -1;
        _sortAscending = true;
        _framesSinceLastRowClick = int.MaxValue;
        _lastClickedRow = null;
        SelectedRow = null;
        CanUserScrollVertical = true;
        _contentState.BackgroundColor = options.Content?.ContentColor ?? Color.White;
    }

    public void SetColumns(IReadOnlyList<ListViewColumn> columns, bool showIconColumn)
    {
        _columns = columns;
        _showIconColumn = showIconColumn;
        _sortColumnIndex = -1;
        _sortAscending = true;
        RecalculateColumns();
        Sort();
    }

    /// <summary>Replaces the rows, keeping the sort, and the selection when the new rows still hold it.</summary>
    public void SetRows(IReadOnlyList<ListViewRow> rows)
    {
        _rows = rows;
        _lastClickedRow = null;
        CutCells();
        Sort();
        UpdateScrollBounds();
        SettleScrollbarsOutsideMeasure();

        if (SelectedRow is { } selected && !rows.Contains(selected))
        {
            Select(null);
        }
    }

    /// <summary>Sorts by columnIndex -- ascending when it isn't already the sort column, reversed when it is.</summary>
    public void SortBy(int columnIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(columnIndex, _columns.Count);

        _sortAscending = columnIndex != _sortColumnIndex || !_sortAscending;
        _sortColumnIndex = columnIndex;
        Sort();
    }

    /// <summary>Selects row (null clears the selection), scrolling it into view.</summary>
    public void Select(ListViewRow? row)
    {
        if (ReferenceEquals(row, SelectedRow))
        {
            return;
        }

        SelectedRow = row;
        if (row is not null)
        {
            ScrollIntoView(Array.IndexOf(_order, IndexOfRow(row)));
        }

        SelectionChanged?.Invoke(row);
    }

    private int IndexOfRow(ListViewRow row)
    {
        for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            if (ReferenceEquals(_rows[rowIndex], row))
            {
                return rowIndex;
            }
        }

        return -1;
    }

    private void Sort()
    {
        RefreshDrawnHeaders();
        _order = [.. Enumerable.Range(0, _rows.Count)];
        if (_sortColumnIndex < 0 || _sortColumnIndex >= _columns.Count)
        {
            return;
        }

        var column = _sortColumnIndex;
        var direction = _sortAscending ? 1 : -1;
        Array.Sort(_order, (left, right) =>
        {
            var comparison = CompareCells(CellOf(_rows[left], column), CellOf(_rows[right], column));
            return comparison != 0 ? comparison * direction : left.CompareTo(right);
        });
    }

    private void RefreshDrawnHeaders()
    {
        _drawnHeaders = new string[_displayHeaders.Length];
        for (var columnIndex = 0; columnIndex < _displayHeaders.Length; columnIndex++)
        {
            _drawnHeaders[columnIndex] = columnIndex == _sortColumnIndex ? _displayHeaders[columnIndex] + (_sortAscending ? " ^" : " v") : _displayHeaders[columnIndex];
        }
    }

    private static string CellOf(ListViewRow row, int columnIndex) => columnIndex < row.Cells.Count ? row.Cells[columnIndex] : string.Empty;

    private static int CompareCells(string left, string right) =>
        double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber) &&
        double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber)
            ? leftNumber.CompareTo(rightNumber)
            : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);

    protected override void RecalculateFixedSize()
    {
        var previousWidth = ContentSize.X;
        base.RecalculateFixedSize();

        if (ContentSize.X != previousWidth)
        {
            RecalculateColumns();
        }

        UpdateScrollBounds();
    }

    private void RecalculateColumns()
    {
        _columnWidths = new float[_columns.Count];
        var used = _showIconColumn ? IconColumnWidth : 0f;
        for (var columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
        {
            _columnWidths[columnIndex] = _columns[columnIndex].Width;
            used += _columns[columnIndex].Width;
        }

        if (_columns.Count > 0)
        {
            _columnWidths[^1] += System.Math.Max(0f, ContentSize.X - used);
        }

        _displayHeaders = new string[_columns.Count];
        for (var columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
        {
            _displayHeaders[columnIndex] = CutToWidth(_columns[columnIndex].Header, _columnWidths[columnIndex] - CellInset * 2 - SortMarkerWidth);
        }

        RefreshDrawnHeaders();

        CutCells();
    }

    private float SortMarkerWidth => _font.MeasureString(" v").X;

    private void CutCells()
    {
        _displayCells = new string[_rows.Count][];
        for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            var cells = new string[_columns.Count];
            for (var columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
            {
                cells[columnIndex] = CutToWidth(CellOf(_rows[rowIndex], columnIndex), _columnWidths.Length > columnIndex ? _columnWidths[columnIndex] - CellInset * 2 : 0f);
            }

            _displayCells[rowIndex] = cells;
        }
    }

    private string CutToWidth(string text, float width) => text.Length == 0 ? text : StringUtility.TruncateWithEllipsis(_textMeasurer, text, width);

    private void UpdateScrollBounds() => SetMaxScrollOffset(new Vector2(0, System.Math.Max(0, HeaderHeight + _rows.Count * RowHeight - ContentSize.Y)));

    private void ScrollIntoView(int displayIndex)
    {
        if (displayIndex < 0)
        {
            return;
        }

        var rowTop = displayIndex * RowHeight;
        var visibleRowsHeight = ContentSize.Y - HeaderHeight;
        if (rowTop < ScrollOffset.Y)
        {
            ScrollTo(new Vector2(0, rowTop));
            return;
        }

        if (rowTop + RowHeight > ScrollOffset.Y + visibleRowsHeight)
        {
            ScrollTo(new Vector2(0, rowTop + RowHeight - visibleRowsHeight));
        }
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_framesSinceLastRowClick < int.MaxValue)
        {
            _framesSinceLastRowClick++;
        }
    }

    protected override void OnContentClickAction(Point mousePosition)
    {
        base.OnContentClickAction(mousePosition);

        var localX = mousePosition.X - ContentAbsolutePosition.X;
        var localY = mousePosition.Y - ContentAbsolutePosition.Y;
        if (localY < HeaderHeight)
        {
            ClickHeader(localX);
            return;
        }

        var displayIndex = (int)((localY - HeaderHeight + ScrollOffset.Y) / RowHeight);
        if (displayIndex < 0 || displayIndex >= _order.Length)
        {
            return;
        }

        var row = _rows[_order[displayIndex]];
        var isSecondClick = ReferenceEquals(row, _lastClickedRow) && _framesSinceLastRowClick <= UiInputController.DoubleClickWindowFrames;
        Select(row);

        if (isSecondClick)
        {
            _lastClickedRow = null;
            _framesSinceLastRowClick = int.MaxValue;
            RowActivated?.Invoke(row);
            return;
        }

        _lastClickedRow = row;
        _framesSinceLastRowClick = 0;
    }

    private void ClickHeader(float localX)
    {
        var x = _showIconColumn ? IconColumnWidth : 0f;
        for (var columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
        {
            x += _columnWidths[columnIndex];
            if (localX < x)
            {
                if (_columns[columnIndex].Sortable)
                {
                    SortBy(columnIndex);
                }

                return;
            }
        }
    }

    protected override void OnKeyPressAction(Keys key)
    {
        if (_order.Length == 0)
        {
            return;
        }

        var current = SelectedRow is { } selected ? Array.IndexOf(_order, IndexOfRow(selected)) : -1;
        switch (key)
        {
            case Keys.Down:
                Select(_rows[_order[System.Math.Min(current + 1, _order.Length - 1)]]);
                break;
            case Keys.Up:
                Select(_rows[_order[System.Math.Max(current - 1, 0)]]);
                break;
            case Keys.Home:
                Select(_rows[_order[0]]);
                break;
            case Keys.End:
                Select(_rows[_order[^1]]);
                break;
            case Keys.Enter when SelectedRow is { } activated:
                RowActivated?.Invoke(activated);
                break;
        }
    }

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = ElementPoolService.SpriteBatch;
        var unitRectangle = ElementPoolService.UnitRectangle;
        var width = ContentSize.X;

        var firstVisible = System.Math.Max(0, (int)(ScrollOffset.Y / RowHeight));
        var lastVisible = System.Math.Min(_order.Length - 1, (int)((ScrollOffset.Y + ContentSize.Y - HeaderHeight) / RowHeight));
        for (var displayIndex = firstVisible; displayIndex <= lastVisible; displayIndex++)
        {
            var rowIndex = _order[displayIndex];
            var row = _rows[rowIndex];
            var top = HeaderHeight + displayIndex * RowHeight;
            if (ReferenceEquals(row, SelectedRow))
            {
                spriteBatch.Draw(unitRectangle, new Rectangle(0, (int)top, (int)width, (int)RowHeight), SelectedRowColor);
            }
            else if (displayIndex % 2 == 1)
            {
                spriteBatch.Draw(unitRectangle, new Rectangle(0, (int)top, (int)width, (int)RowHeight), AlternateRowColor);
            }

            var x = 0f;
            if (_showIconColumn)
            {
                if (row.IconGlyph is { Length: > 0 } glyph)
                {
                    LabelRenderer.DrawCentered(spriteBatch, _font, glyph, new Vector2(x, top), new Vector2(IconColumnWidth, RowHeight), row.IconColor ?? WindowPalette.BodyTextColor);
                }

                x += IconColumnWidth;
            }

            var cells = _displayCells[rowIndex];
            for (var columnIndex = 0; columnIndex < cells.Length; columnIndex++)
            {
                if (cells[columnIndex].Length > 0)
                {
                    LabelRenderer.DrawLeftAligned(spriteBatch, _font, cells[columnIndex], new Vector2(x + CellInset, top), new Vector2(_columnWidths[columnIndex] - CellInset * 2, RowHeight), WindowPalette.BodyTextColor);
                }

                x += _columnWidths[columnIndex];
            }
        }

        DrawHeader(spriteBatch, unitRectangle, width);
    }

    /// <summary>Drawn last, over any row scrolled up beneath it, at the top of what is shown.</summary>
    private void DrawHeader(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch, Microsoft.Xna.Framework.Graphics.Texture2D unitRectangle, float width)
    {
        var top = ScrollOffset.Y;
        spriteBatch.Draw(unitRectangle, new Rectangle(0, (int)top, (int)width, (int)HeaderHeight), HeaderColor);

        var x = _showIconColumn ? IconColumnWidth : 0f;
        for (var columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
        {
            var columnWidth = _columnWidths[columnIndex];
            LabelRenderer.DrawLeftAligned(spriteBatch, _font, _drawnHeaders[columnIndex], new Vector2(x + CellInset, top), new Vector2(columnWidth - CellInset * 2, HeaderHeight), HeaderTextColor);
            spriteBatch.Draw(unitRectangle, new Rectangle((int)(x + columnWidth) - 1, (int)top + 3, 1, (int)HeaderHeight - 6), WindowPalette.ScrollbarThumbColor);
            x += columnWidth;
        }
    }
}
