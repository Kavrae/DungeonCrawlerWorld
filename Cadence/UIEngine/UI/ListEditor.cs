using Microsoft.Xna.Framework;
using UIEngine.UI.Chrome;

namespace UIEngine.UI;

/// <summary>
/// The rows of an editable list -- each row's content beside its own move up, move down and remove buttons, and an add
/// button under them all. The owner fills each row's content and owns the list: a button asks (AddRequested,
/// RemoveRequested, MoveRequested), the owner changes its data and calls SetRows again.
/// </summary>
/// <remarks>
/// A request is raised on the update after the click, never inside it, since the owner answering it rebuilds every row
/// -- including the button whose click is still being handled. Before it is raised, a text box focused in a row is
/// submitted (TextBox.Submit), so what was typed reaches the owner rather than being lost to the rebuild. The rows scroll
/// when they outgrow the editor; the add button stays below them, always shown. Configure before Initialize, the usual
/// contract.
/// </remarks>
public sealed class ListEditor(ElementServices services)
    : Element(services)
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
    private Window _rowsHost = null!;

    public int RowCount => _rowCount;

    /// <summary>The content host of each row, in order.</summary>
    public IEnumerable<Window> RowContents => _rows.Select(row => row.Content);

    /// <summary>Every row's buttons, row by row: its move up, move down and remove.</summary>
    public IEnumerable<Button> RowButtons => _rows.SelectMany(row => new[] { row.MoveUp, row.MoveDown, row.Remove });

    public Button AddButton { get; private set; } = null!;

    /// <summary>Whether the rows are taller than the space above the add button, so they scroll.</summary>
    public bool ShowsRowScrollbar => _rowsHost.ShowVerticalScrollbar;

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
    }

    public override void Initialize()
    {
        base.Initialize();

        _rowsHost = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = Vector2.One, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false, CanUserScrollVertical = true },
        });
        _rowsHost.ContentPadding = Vector2.Zero;
        AddChild(_rowsHost);

        AddButton = CreateButton(this, _addLabel, enabled: true, () => AddRequested?.Invoke());

        ContentResized += _ => LayOutRowsHostAndAddButton();
        _rowsHost.ContentResized += _ => OnRowsHostResized();
        LayOutRowsHostAndAddButton();
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

    private void LayOutRowsHostAndAddButton()
    {
        var addButtonTop = System.Math.Max(0f, ContentSize.Y - _rowHeight);
        _rowsHost.SetBounds(Vector2.Zero, new Vector2(ContentSize.X, System.Math.Max(1f, addButtonTop - RowGap)));
        AddButton.SetBounds(new Vector2(0, addButtonTop), new Vector2(AddButtonWidth, _rowHeight));
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
        ElementPoolService.CloseAllChildren(_rowsHost);
        _rows.Clear();

        for (var rowIndex = 0; rowIndex < _rowCount; rowIndex++)
        {
            var capturedIndex = rowIndex;
            var content = ElementPoolService.CreateElement<Window>(_rowsHost, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
                Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(1, _rowHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            });
            content.ContentPadding = Vector2.Zero;
            _rowsHost.AddChild(content);

            var moveUp = CreateButton(_rowsHost, MoveUpLabel, enabled: rowIndex > 0, () => MoveRequested?.Invoke(capturedIndex, capturedIndex - 1));
            var moveDown = CreateButton(_rowsHost, MoveDownLabel, enabled: rowIndex < _rowCount - 1, () => MoveRequested?.Invoke(capturedIndex, capturedIndex + 1));
            var remove = CreateButton(_rowsHost, RemoveLabel, enabled: true, () => RemoveRequested?.Invoke(capturedIndex));
            _rows.Add((content, moveUp, moveDown, remove));
        }
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
    private void OnRowsHostResized()
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

    private float RowsHeight => _rowCount * (_rowHeight + RowGap);

    /// <summary>
    /// The width of a row's content: what the buttons leave of the rows' width with no scrollbar, less the scrollbar's when
    /// the rows are taller than the space they have -- decided from the rows themselves, so a row is sized for the
    /// scrollbar before it appears rather than after.
    /// </summary>
    private float RowContentWidth
    {
        get
        {
            var shownScrollbarWidth = _rowsHost.ShowVerticalScrollbar ? WindowChrome.ScrollbarThickness : 0f;
            var widthWithoutScrollbar = _rowsHost.ContentSize.X + shownScrollbarWidth;
            var scrollbarWidth = RowsHeight > _rowsHost.ContentSize.Y ? WindowChrome.ScrollbarThickness : 0f;
            return System.Math.Max(1f, widthWithoutScrollbar - scrollbarWidth - RowButtonWidth * 3 - RowGap * 3);
        }
    }

    private Button CreateButton(Element parent, string label, bool enabled, Action request)
    {
        var button = ElementPoolService.CreateElement<Button>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(RowButtonWidth, _rowHeight), DisplayMode = ElementDisplayMode.Fixed },
        });
        button.SetText(label);
        button.Enabled = enabled;
        button.Clicked += _ => _pendingRequest = request;
        parent.AddChild(button);
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
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_pendingRequest is { } request)
        {
            _pendingRequest = null;
            FindFocusedTextBox(_rowsHost)?.Submit();
            request();
        }
    }

    private static TextBox? FindFocusedTextBox(Element element)
    {
        foreach (var child in element.ChildElements)
        {
            if (child is TextBox { IsFocused: true } focusedTextBox)
            {
                return focusedTextBox;
            }

            if (FindFocusedTextBox(child) is { } focusedDescendant)
            {
                return focusedDescendant;
            }
        }

        return null;
    }
}
