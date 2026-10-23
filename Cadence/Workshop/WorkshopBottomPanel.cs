using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace Workshop;

/// <summary>Which list the bottom panel shows.</summary>
public enum WorkshopBottomPanelTab : byte
{
    ErrorList,
    Dependents,
}

/// <summary>
/// The panel along the IDE's bottom edge: the Error List (what went wrong loading the content) and Dependents (what names
/// the record selected in the open tab), one at a time under their two tab tiles. Activating a line that names a record a
/// tab shows opens it there. Its top edge drags to make it taller or shorter.
/// </summary>
/// <remarks>Configure before Initialize, the usual contract. Created and laid out by WorkshopShell.</remarks>
public sealed class WorkshopBottomPanel(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
    : Window(fontService, elementPoolService, renderService)
{
    public const float MinimumHeight = 80f;
    public const float GripHeight = 5f;
    private const float TileHeight = 24f;
    private const float TileHorizontalPadding = 12f;

    public const string ErrorListTitle = "Error List";
    public const string DependentsTitle = "Dependents";
    private const string ErrorGlyph = "x";
    private const string WarningGlyph = "!";
    private const string MessageGlyph = "i";

    private static readonly ListViewColumn[] ErrorListColumns = [new("Description", 600), new("Source", 260)];
    private static readonly ListViewColumn[] DependentsColumns = [new("Name", 260), new("Kind", 120), new("How", 200)];

    private static readonly Color SelectedTileColor = WindowPalette.ControlBackground;
    private static readonly Color SelectedTileTextColor = Color.White;

    private readonly SpriteFontBase _tileFont = fontService.GetFont(FontChrome.DefaultFontSize);

    private TabNavigator _navigator = null!;
    private TextWindow _errorListTile = null!;
    private TextWindow _dependentsTile = null!;
    private ListView _list = null!;
    private IReadOnlyList<ListViewRow> _problemRows = [];
    private IReadOnlyList<ListViewRow> _dependentRows = [];
    private float? _dragStartHeight;

    /// <summary>The tab whose columns the list has, or null before the first is shown.</summary>
    private WorkshopBottomPanelTab? _columnsShownFor;

    public WorkshopBottomPanelTab ShownTab { get; private set; }

    /// <summary>The list the shown tab fills.</summary>
    public ListView List => _list;

    /// <summary>Raised with the panel's new height while its top edge is dragged.</summary>
    public event Action<float>? HeightDragged;

    public void Configure(TabNavigator navigator)
    {
        _navigator = navigator;
        _problemRows = [];
        _dependentRows = [];
        _dragStartHeight = null;
        _columnsShownFor = null;
        ShownTab = WorkshopBottomPanelTab.ErrorList;
    }

    public override void Initialize()
    {
        base.Initialize();

        ContentPadding = Vector2.Zero;
        _errorListTile = CreateTile(ErrorListTitle, () => ShowTab(WorkshopBottomPanelTab.ErrorList));
        _dependentsTile = CreateTile(DependentsTitle, () => ShowTab(WorkshopBottomPanelTab.Dependents));

        _list = ElementPoolService.CreateElement<ListView>(this, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, GripHeight + TileHeight), Size = ListSize(), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false },
        });
        _list.RowActivated += OpenRecordOf;
        AddChild(_list);

        ContentResized += _ => LayOut();
        LayOut();
        ShowTab(ShownTab);
    }

    private TextWindow CreateTile(string label, Action onClicked)
    {
        var size = new Vector2(1, TileHeight);
        var tile = ElementPoolService.CreateElement<TextWindow>(this, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, GripHeight), Size = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Inset, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
            Text = new TextOptions { Text = label, TextColor = WindowPalette.BodyTextColor },
        });
        tile.ContentFont = _tileFont;
        tile.Clicked += _ => onClicked();
        AddChild(tile);
        return tile;
    }

    /// <summary>Replaces the Error List's lines, newest load first.</summary>
    public void SetProblems(IReadOnlyList<WorkshopProblem> problems)
    {
        _problemRows = [.. problems.Select(problem => new ListViewRow([problem.Description, problem.Source], GlyphOf(problem.Severity), ColorOf(problem.Severity), problem))];
        _errorListTile.UpdateText(problems.Count == 0 ? ErrorListTitle : $"{ErrorListTitle} ({problems.Count})");
        LayOut();
        RefreshShownRows();
    }

    /// <summary>Shows what names subject, the record selected in the open tab.</summary>
    public void ShowDependents(string subject, IReadOnlyList<WorkshopDependent> dependents)
    {
        _dependentRows = [.. dependents.Select(dependent => new ListViewRow([dependent.Name, dependent.Kind, dependent.Relationship], Tag: dependent))];
        _dependentsTile.UpdateText($"{DependentsTitle}: {subject} ({dependents.Count})");
        LayOut();
        RefreshShownRows();
    }

    /// <summary>Empties Dependents, and offers its tile only when isOffered -- the open tab has nothing selected yet, or no records at all. Showing the Error List when Dependents goes.</summary>
    public void ClearDependents(bool isOffered)
    {
        _dependentsTile.IsVisible = isOffered;
        if (!isOffered && ShownTab == WorkshopBottomPanelTab.Dependents)
        {
            ShowTab(WorkshopBottomPanelTab.ErrorList);
        }

        _dependentRows = [];
        _dependentsTile.UpdateText(DependentsTitle);
        LayOut();
        RefreshShownRows();
    }

    public void ShowTab(WorkshopBottomPanelTab tab)
    {
        var columnsChanged = tab != _columnsShownFor;
        ShownTab = tab;
        _columnsShownFor = tab;
        StyleTile(_errorListTile, tab == WorkshopBottomPanelTab.ErrorList);
        StyleTile(_dependentsTile, tab == WorkshopBottomPanelTab.Dependents);
        if (columnsChanged)
        {
            _list.SetColumns(tab == WorkshopBottomPanelTab.ErrorList ? ErrorListColumns : DependentsColumns, showIconColumn: tab == WorkshopBottomPanelTab.ErrorList);
        }

        RefreshShownRows();
    }

    private void RefreshShownRows() => _list.SetRows(ShownTab == WorkshopBottomPanelTab.ErrorList ? _problemRows : _dependentRows);

    private void OpenRecordOf(ListViewRow row)
    {
        var (tabTitle, recordId) = row.Tag switch
        {
            WorkshopProblem problem => (problem.TabTitle, problem.RecordId),
            WorkshopDependent dependent => (dependent.TabTitle, dependent.RecordId),
            _ => (null, null),
        };

        if (tabTitle is not null && recordId is { } id)
        {
            _navigator.RequestOpenRecord(tabTitle, id);
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

    private static void StyleTile(TextWindow tile, bool isSelected)
    {
        tile.BorderStyle = isSelected ? BorderStyle.Outset : BorderStyle.Inset;
        tile.SetContentColor(isSelected ? SelectedTileColor : WindowPalette.PanelContentColor);
        tile.TextColor = isSelected ? SelectedTileTextColor : WindowPalette.BodyTextColor;
    }

    private Vector2 ListSize() => new(System.Math.Max(1f, ContentSize.X), System.Math.Max(1f, ContentSize.Y - GripHeight - TileHeight));

    private void LayOut()
    {
        var x = 0f;
        ReadOnlySpan<TextWindow> tiles = [_errorListTile, _dependentsTile];
        foreach (var tile in tiles)
        {
            var width = _tileFont.MeasureString(tile.OriginalText).X + TileHorizontalPadding * 2;
            tile.SetBounds(new Vector2(x, GripHeight), new Vector2(width, TileHeight));
            x += width;
        }

        _list.SetBounds(new Vector2(0, GripHeight + TileHeight), ListSize());
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
