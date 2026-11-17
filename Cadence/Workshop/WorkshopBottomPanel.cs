using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Fonts;
using UIEngine.UI;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace Workshop;

/// <summary>Which list the bottom panel shows.</summary>
public enum WorkshopBottomPanelTab : byte
{
    ErrorList,
    UsedBy,
    Uses,
    Changes,
    ToPublish,
}

/// <summary>
/// The panel along the IDE's bottom edge: the Error List (what went wrong loading the content), To Publish (every record
/// a publish would write, offered by a host that edits), Used By (what names the record selected in the open tab), Uses
/// (what it names) and Changes (how it differs from what it is compared with, offered by a tab that edits), one at a
/// time under their tab tiles. Activating a line that names a record a tab shows opens it there -- in the Error List,
/// To Publish and Changes one click does, Changes taking the player to the field; a right-click on a Changes line
/// offers to revert it. Its top edge drags to make it taller or shorter.
/// </summary>
/// <remarks>Configure before Initialize, the usual contract. Created and laid out by WorkshopShell.</remarks>
public sealed class WorkshopBottomPanel(ElementServices services)
    : Window(services)
{
    public const float MinimumHeight = 80f;
    public const float GripHeight = 5f;
    private const float TileHeight = 24f;
    private const float TileHorizontalPadding = 12f;

    /// <summary>The space between the tiles about the whole Workshop (Error List, To Publish) and those about the selected record (Used By, Uses, Changes).</summary>
    private const float TileGroupGap = 24f;

    public const string ErrorListTitle = "Error List";
    public const string UsedByTitle = "Used By";
    public const string UsesTitle = "Uses";
    public const string ChangesTitle = "Changes";
    public const string ToPublishTitle = "To Publish";
    private const string CurrentColumnHeader = "Unsaved";
    private const string NothingToPublishText = "Nothing to publish: master has everything.";
    private const string ErrorGlyph = "x";
    private const string WarningGlyph = "!";
    private const string MessageGlyph = "i";

    private static readonly ListViewColumn[] ErrorListColumns = [new("Description", 600), new("Source", 260)];
    private static readonly ListViewColumn[] RelatedRecordColumns = [new("Name", 260), new("Kind", 120), new("How", 260)];
    private static readonly ListViewColumn[] PendingRecordColumns = [new("Record", 300), new("Table", 120), new("Change", 220), new("State", 300)];

    private static readonly Color SelectedTileColor = WindowPalette.ControlBackground;
    private static readonly Color SelectedTileTextColor = Color.White;

    private readonly SpriteFontBase _tileFont = services.FontService.GetFont(FontChrome.DefaultFontSize);

    private TabNavigator _navigator = null!;
    private ContextMenuController _contextMenus = null!;
    private Button _errorListTile = null!;
    private Button _usedByTile = null!;
    private Button _usesTile = null!;
    private Button _changesTile = null!;
    private Button _toPublishTile = null!;
    private IReadOnlyList<ListViewRow> _pendingRows = [];
    private ListView _list = null!;
    private IReadOnlyList<ListViewRow> _problemRows = [];
    private IReadOnlyList<ListViewRow> _usedByRows = [];
    private IReadOnlyList<ListViewRow> _usesRows = [];
    private IReadOnlyList<ListViewRow> _changeRows = [];
    private string _changesBaselineName = string.Empty;
    private float? _dragStartHeight;

    /// <summary>Which columns the list has -- the Error List's, Used By and Uses', or Changes' with its baseline's name -- or null before the first tab is shown.</summary>
    private string? _columnsShownFor;

    public WorkshopBottomPanelTab ShownTab { get; private set; }

    /// <summary>The list the shown tab fills.</summary>
    public ListView List => _list;

    /// <summary>Raised with the panel's new height while its top edge is dragged.</summary>
    public event Action<float>? HeightDragged;

    public void Configure(TabNavigator navigator, ContextMenuController contextMenus)
    {
        _navigator = navigator;
        _contextMenus = contextMenus;
        _problemRows = [];
        _usedByRows = [];
        _usesRows = [];
        _changeRows = [];
        _pendingRows = [];
        _changesBaselineName = string.Empty;
        _dragStartHeight = null;
        _columnsShownFor = null;
        ShownTab = WorkshopBottomPanelTab.ErrorList;
    }

    public override void Initialize()
    {
        base.Initialize();

        ContentPadding = Vector2.Zero;
        _errorListTile = CreateTile(ErrorListTitle, () => ShowTab(WorkshopBottomPanelTab.ErrorList));
        _toPublishTile = CreateTile(ToPublishTitle, () => ShowTab(WorkshopBottomPanelTab.ToPublish));
        _toPublishTile.IsVisible = false;
        _usedByTile = CreateTile(UsedByTitle, () => ShowTab(WorkshopBottomPanelTab.UsedBy));
        _usesTile = CreateTile(UsesTitle, () => ShowTab(WorkshopBottomPanelTab.Uses));
        _changesTile = CreateTile(ChangesTitle, () => ShowTab(WorkshopBottomPanelTab.Changes));
        _changesTile.IsVisible = false;

        _list = ElementPoolService.CreateElement<ListView>(this, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, GripHeight + TileHeight), Size = ListSize(), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false },
        });
        _list.RowActivated += OpenRecordOf;
        _list.RowClicked += row =>
        {
            if (row.Tag is WorkshopProblem or WorkshopFieldChange or WorkshopPendingRecord)
            {
                OpenRecordOf(row);
            }
        };
        _list.OnRightClicked = OpenChangeMenu;
        AddChild(_list);

        ContentResized += _ => LayOut();
        LayOut();
        ShowTab(ShownTab);
    }

    private Button CreateTile(string label, Action onClicked)
    {
        var size = new Vector2(1, TileHeight);
        var tile = ElementPoolService.CreateElement<Button>(this, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, GripHeight), Size = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Inset, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
            Text = new TextOptions { TextColor = WindowPalette.BodyTextColor },
        });
        tile.ContentFont = _tileFont;
        tile.SetText(label);
        tile.Clicked += _ => onClicked();
        AddChild(tile);
        return tile;
    }

    /// <summary>Replaces the Error List's lines, newest load first.</summary>
    public void SetProblems(IReadOnlyList<WorkshopProblem> problems)
    {
        _problemRows = [.. problems.Select(problem => new ListViewRow([problem.Description, problem.Source], GlyphOf(problem.Severity), ColorOf(problem.Severity), problem))];
        _errorListTile.SetText(problems.Count == 0 ? ErrorListTitle : $"{ErrorListTitle} ({problems.Count})");
        LayOut();
        RefreshShownRows();
    }

    /// <summary>Shows what names the record selected in the open tab (Used By), and what it names (Uses).</summary>
    public void ShowRelatedRecords(IReadOnlyList<WorkshopRelatedRecord> usedBy, IReadOnlyList<WorkshopRelatedRecord> uses)
    {
        _usedByRows = RowsOf(usedBy);
        _usesRows = RowsOf(uses);
        _usedByTile.SetText($"{UsedByTitle} ({usedBy.Count})");
        _usesTile.SetText($"{UsesTitle} ({uses.Count})");
        LayOut();
        RefreshShownRows();
    }

    private static IReadOnlyList<ListViewRow> RowsOf(IReadOnlyList<WorkshopRelatedRecord> records) =>
        [.. records.Select(record => new ListViewRow([record.Name, record.Kind, record.Relationship], Tag: record))];

    /// <summary>Empties Used By and Uses, and offers their tiles only when isOffered -- the open tab has nothing selected yet, or no records at all; showing the Error List when they go. Changes goes until the open tab shows some (ShowChanges).</summary>
    public void ClearRelatedRecords(bool isOffered)
    {
        _usedByTile.IsVisible = isOffered;
        _usesTile.IsVisible = isOffered;
        _changesTile.IsVisible = false;
        if ((!isOffered && ShownTab != WorkshopBottomPanelTab.ErrorList) || ShownTab == WorkshopBottomPanelTab.Changes)
        {
            ShowTab(WorkshopBottomPanelTab.ErrorList);
        }

        _usedByRows = [];
        _usesRows = [];
        _changeRows = [];
        _usedByTile.SetText(UsedByTitle);
        _usesTile.SetText(UsesTitle);
        _changesTile.SetText(ChangesTitle);
        LayOut();
        RefreshShownRows();
    }

    /// <summary>Shows how the record selected in the open tab differs from what it is compared with, offering the Changes tile -- or, for null, taking it away (a record with nothing to compare), showing the Error List if Changes was shown.</summary>
    public void ShowChanges(WorkshopChanges? changes)
    {
        _changesTile.IsVisible = changes is not null;
        if (changes is null)
        {
            _changeRows = [];
            _changesTile.SetText(ChangesTitle);
            if (ShownTab == WorkshopBottomPanelTab.Changes)
            {
                ShowTab(WorkshopBottomPanelTab.ErrorList);
            }

            LayOut();
            return;
        }

        _changeRows = changes.Changes.Count == 0
            ? [new ListViewRow([changes.NothingChangedText, string.Empty, string.Empty, string.Empty])]
            : [.. changes.Changes.Select(change => new ListViewRow([change.Field, change.Change, change.Baseline, change.Current], Tag: change))];
        _changesTile.SetText(changes.Changes.Count == 0 ? ChangesTitle : $"{ChangesTitle} ({changes.Changes.Count})");
        var baselineChanged = _changesBaselineName != changes.BaselineName;
        _changesBaselineName = changes.BaselineName;
        LayOut();
        if (baselineChanged && ShownTab == WorkshopBottomPanelTab.Changes)
        {
            ShowTab(WorkshopBottomPanelTab.Changes);
            return;
        }

        RefreshShownRows();
    }

    /// <summary>Shows every record a publish would write, offering the To Publish tile from now on -- a host that edits sends it whenever what it holds changes.</summary>
    public void SetPendingPublish(IReadOnlyList<WorkshopPendingRecord> records)
    {
        _toPublishTile.IsVisible = true;
        _pendingRows = records.Count == 0
            ? [new ListViewRow([NothingToPublishText, string.Empty, string.Empty, string.Empty])]
            : [.. records.Select(record => new ListViewRow([record.Name, record.Table, record.Change, record.State], Tag: record))];
        _toPublishTile.SetText(records.Count == 0 ? ToPublishTitle : $"{ToPublishTitle} ({records.Count})");
        LayOut();
        RefreshShownRows();
    }

    public void ShowTab(WorkshopBottomPanelTab tab)
    {
        var columnsKey = tab switch
        {
            WorkshopBottomPanelTab.ErrorList => ErrorListTitle,
            WorkshopBottomPanelTab.Changes => $"{ChangesTitle}:{_changesBaselineName}",
            WorkshopBottomPanelTab.ToPublish => ToPublishTitle,
            _ => UsedByTitle,
        };
        ShownTab = tab;
        StyleTile(_errorListTile, tab == WorkshopBottomPanelTab.ErrorList);
        StyleTile(_toPublishTile, tab == WorkshopBottomPanelTab.ToPublish);
        StyleTile(_usedByTile, tab == WorkshopBottomPanelTab.UsedBy);
        StyleTile(_usesTile, tab == WorkshopBottomPanelTab.Uses);
        StyleTile(_changesTile, tab == WorkshopBottomPanelTab.Changes);
        if (columnsKey != _columnsShownFor)
        {
            _columnsShownFor = columnsKey;
            _list.SetColumns(tab switch
            {
                WorkshopBottomPanelTab.ErrorList => ErrorListColumns,
                WorkshopBottomPanelTab.Changes => ChangeColumns(_changesBaselineName),
                WorkshopBottomPanelTab.ToPublish => PendingRecordColumns,
                _ => RelatedRecordColumns,
            }, showIconColumn: tab == WorkshopBottomPanelTab.ErrorList);
        }

        RefreshShownRows();
    }

    private static ListViewColumn[] ChangeColumns(string baselineName) =>
        [new("Field", 420), new("Change", 90), new(baselineName, 260), new(CurrentColumnHeader, 260)];

    private void RefreshShownRows() => _list.SetRows(ShownTab switch
    {
        WorkshopBottomPanelTab.UsedBy => _usedByRows,
        WorkshopBottomPanelTab.Uses => _usesRows,
        WorkshopBottomPanelTab.Changes => _changeRows,
        WorkshopBottomPanelTab.ToPublish => _pendingRows,
        _ => _problemRows,
    });

    /// <summary>A right-click on a Changes line: Revert, putting that field back as the baseline has it -- what the row's revert button in the inspector does.</summary>
    private void OpenChangeMenu(Point position)
    {
        if (ShownTab != WorkshopBottomPanelTab.Changes || _list.RowAt(position) is not { Tag: WorkshopFieldChange change })
        {
            return;
        }

        _contextMenus.Open(new Vector2(position.X, position.Y), [new DropdownOption($"Revert to {_changesBaselineName}", null, change.Revert is not null, () => change.Revert?.Invoke())]);
    }

    private void OpenRecordOf(ListViewRow row)
    {
        var (tabTitle, recordId, fieldPath) = row.Tag switch
        {
            WorkshopProblem problem => (problem.TabTitle, problem.RecordId, problem.FieldPath),
            WorkshopRelatedRecord dependent => (dependent.TabTitle, dependent.RecordId, (string?)null),
            WorkshopFieldChange change => (change.TabTitle, change.RecordId, change.FieldPath),
            WorkshopPendingRecord pending => (pending.TabTitle, pending.RecordId, (string?)null),
            _ => (null, null, null),
        };

        if (tabTitle is not null && recordId is { } id)
        {
            _navigator.RequestOpenRecord(tabTitle, id, fieldPath);
        }
    }

    internal static string GlyphOf(WorkshopProblemSeverity severity) => severity switch
    {
        WorkshopProblemSeverity.Error => ErrorGlyph,
        WorkshopProblemSeverity.Warning => WarningGlyph,
        _ => MessageGlyph,
    };

    internal static Color ColorOf(WorkshopProblemSeverity severity) => severity switch
    {
        WorkshopProblemSeverity.Error => Color.Red,
        WorkshopProblemSeverity.Warning => Color.DarkGoldenrod,
        _ => Color.SteelBlue,
    };

    private static void StyleTile(Button tile, bool isSelected)
    {
        tile.RestingBorderStyle = isSelected ? BorderStyle.Outset : BorderStyle.Inset;
        tile.SetContentColor(isSelected ? SelectedTileColor : WindowPalette.PanelContentColor);
        tile.TextColor = isSelected ? SelectedTileTextColor : WindowPalette.BodyTextColor;
    }

    private Vector2 ListSize() => new(System.Math.Max(1f, ContentSize.X), System.Math.Max(1f, ContentSize.Y - GripHeight - TileHeight));

    private void LayOut()
    {
        var x = LayOutTiles([_errorListTile, _toPublishTile], 0f);
        LayOutTiles([_usedByTile, _usesTile, _changesTile], x == 0f ? 0f : x + TileGroupGap);

        _list.SetBounds(new Vector2(0, GripHeight + TileHeight), ListSize());
    }

    /// <summary>Lays the visible tiles out left to right from x, returning where the next would go.</summary>
    private float LayOutTiles(ReadOnlySpan<Button> tiles, float x)
    {
        foreach (var tile in tiles)
        {
            if (!tile.IsVisible)
            {
                continue;
            }

            var width = _tileFont.MeasureString(tile.Text).X + TileHorizontalPadding * 2;
            tile.SetBounds(new Vector2(x, GripHeight), new Vector2(width, TileHeight));
            x += width;
        }

        return x;
    }

    private Rectangle GripRectangle => new(Rectangle.X, Rectangle.Y, Rectangle.Width, (int)GripHeight + 1);

    protected override bool IsPointerDragHandleAt(Point position) => GripRectangle.Contains(position);

    protected override MouseCursor PointerDragCursor => MouseCursor.SizeNS;

    protected override void OnPointerDragAction(Vector2 totalMouseDelta)
    {
        _dragStartHeight ??= CurrentSize.Y;
        HeightDragged?.Invoke(System.Math.Max(MinimumHeight, _dragStartHeight.Value - totalMouseDelta.Y));
    }

    protected override void OnPointerDragEndedAction() => _dragStartHeight = null;
}
