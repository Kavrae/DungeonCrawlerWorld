using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;

namespace UIEngine.UI;

/// <summary>
/// The rows of an editable list -- each row's content beside its own move up, move down and remove buttons, and an add
/// button under the last. The owner fills each row's content and owns the list: a button asks (AddRequested,
/// RemoveRequested, MoveRequested), the owner changes its data and calls SetRows again.
/// </summary>
/// <remarks>
/// A request is raised on the update after the click, never inside it, since the owner answering it rebuilds every row
/// -- including the button whose click is still being handled. Scrolls when the rows outgrow it. Configure before
/// Initialize, the usual contract.
/// </remarks>
public sealed class ListEditor(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer)
    : Element(fontService, elementPoolService, labelRenderer)
{
    public const float RowButtonWidth = 20f;
    private const float RowGap = 2f;
    private const float AddButtonWidth = 60f;

    public const string MoveUpLabel = "^";
    public const string MoveDownLabel = "v";
    public const string RemoveLabel = "X";

    private float _rowHeight = 24f;
    private string _addLabel = "Add";
    private int _rowCount;
    private Action<Window, int>? _fillRow;
    private Action? _pendingRequest;
    private float _filledRowContentWidth;
    private bool _isRebuildingRows;

    private readonly List<(Window Content, Button MoveUp, Button MoveDown, Button Remove)> _rows = [];
    private Button? _addButton;

    public int RowCount => _rowCount;

    /// <summary>The content host of each row, in order.</summary>
    public IEnumerable<Window> RowContents => _rows.Select(row => row.Content);

    public event Action? AddRequested;

    /// <summary>Raised with the row to remove.</summary>
    public event Action<int>? RemoveRequested;

    /// <summary>Raised with the row to move and where it goes.</summary>
    public event Action<int, int>? MoveRequested;

    public void Configure(float rowHeight, string addLabel = "Add")
    {
        _rowHeight = rowHeight;
        _addLabel = addLabel;
        _rowCount = 0;
        _fillRow = null;
        _pendingRequest = null;
        _filledRowContentWidth = 0f;
        _isRebuildingRows = false;
        _rows.Clear();
        _addButton = null;
    }

    public override void Initialize()
    {
        base.Initialize();

        CanUserScrollVertical = true;
        ContentResized += _ => OnContentResized();
        RebuildRows();
    }

    /// <summary>Rebuilds every row: count of them, each filled by fillRow with its content host and its index.</summary>
    public void SetRows(int count, Action<Window, int> fillRow)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(fillRow);

        _rowCount = count;
        _fillRow = fillRow;
        RebuildRows();
    }

    private void RebuildRows()
    {
        _isRebuildingRows = true;
        try
        {
            RebuildRowsWithoutRefilling();
            LayOutRows();
        }
        finally
        {
            _isRebuildingRows = false;
        }

        FillRows();
    }

    private void RebuildRowsWithoutRefilling()
    {
        ElementPoolService.CloseAllChildren(this);
        _rows.Clear();

        for (var rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            var capturedIndex = rowIndex;
            var content = ElementPoolService.CreateElement<Window>(this, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
                Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(1, _rowHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            });
            content.ContentPadding = Vector2.Zero;
            AddChild(content);

            var moveUp = AddRowButton(MoveUpLabel, enabled: rowIndex > 0, () => MoveRequested?.Invoke(capturedIndex, capturedIndex - 1));
            var moveDown = AddRowButton(MoveDownLabel, enabled: rowIndex < _rowCount - 1, () => MoveRequested?.Invoke(capturedIndex, capturedIndex + 1));
            var remove = AddRowButton(RemoveLabel, enabled: true, () => RemoveRequested?.Invoke(capturedIndex));
            _rows.Add((content, moveUp, moveDown, remove));
        }

        _addButton = AddRowButton(_addLabel, enabled: true, () => AddRequested?.Invoke());
    }

    /// <summary>Fills every row's content at the width it has now, remembering that width so a later change refills it.</summary>
    private void FillRows()
    {
        _filledRowContentWidth = RowContentWidth;
        for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            _fillRow?.Invoke(_rows[rowIndex].Content, rowIndex);
        }
    }

    /// <summary>Lays the rows out again, and refills their content when its width changed -- what the owner built was sized to the old one.</summary>
    private void OnContentResized()
    {
        if (_isRebuildingRows)
        {
            return;
        }

        LayOutRows();
        if (RowContentWidth == _filledRowContentWidth)
        {
            return;
        }

        foreach (var row in _rows)
        {
            ElementPoolService.CloseAllChildren(row.Content);
        }

        FillRows();
    }

    /// <summary>The rows' height with the add button under them.</summary>
    private float RowsHeight => (_rowCount + 1) * (_rowHeight + RowGap);

    /// <summary>
    /// The width of a row's content: what the buttons leave of the width with no scrollbar, less the scrollbar's when the
    /// rows are taller than the editor -- decided from the rows themselves, so a row is sized for the scrollbar before it
    /// appears rather than after.
    /// </summary>
    private float RowContentWidth
    {
        get
        {
            var widthWithoutScrollbar = ContentSize.X + ScrollbarGutterSize.X;
            var scrollbarWidth = RowsHeight > ContentSize.Y + ScrollbarGutterSize.Y ? WindowChrome.ScrollbarThickness : 0f;
            return System.Math.Max(1f, widthWithoutScrollbar - scrollbarWidth - RowButtonWidth * 3 - RowGap * 3);
        }
    }

    private Button AddRowButton(string label, bool enabled, Action request)
    {
        var button = ElementPoolService.CreateElement<Button>(this, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(RowButtonWidth, _rowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Text = new TextOptions { Text = label },
        });
        button.Enabled = enabled;
        button.Clicked += _ => _pendingRequest = request;
        AddChild(button);
        return button;
    }

    private void LayOutRows()
    {
        var contentWidth = RowContentWidth;
        for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            var (content, moveUp, moveDown, remove) = _rows[rowIndex];
            var y = rowIndex * (_rowHeight + RowGap);
            content.SetBounds(new Vector2(0, y), new Vector2(contentWidth, _rowHeight));
            var x = contentWidth + RowGap;
            ReadOnlySpan<Button> rowButtons = [moveUp, moveDown, remove];
            foreach (var button in rowButtons)
            {
                button.SetBounds(new Vector2(x, y), new Vector2(RowButtonWidth, _rowHeight));
                x += RowButtonWidth + RowGap;
            }
        }

        _addButton?.SetBounds(new Vector2(0, _rows.Count * (_rowHeight + RowGap)), new Vector2(AddButtonWidth, _rowHeight));
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_pendingRequest is { } request)
        {
            _pendingRequest = null;
            request();
        }
    }
}
