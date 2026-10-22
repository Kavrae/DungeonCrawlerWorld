using Builders.Browsing;
using Builders.References;
using Engine.Tags;
using Game.Areas;
using Microsoft.Xna.Framework;
using UIEngine.Input;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;
using Workshop;

namespace Builders.MapAreas;

/// <summary>The Map tab: every area, palette and fixed placement, the selected area shown as the game builds it (MapPreviewWindow), the selected record's details (MapDetailsWindow) with the hovered cell's under them (CellDetailsWindow), and what it uses and is used by -- read-only.</summary>
/// <remarks>
/// Its preview is wider than the other tabs', so a 32x32 area fits whole at the map's middle zoom. The preview and details
/// titles name the kind selected (Area, Palette, Placement). Cell Details sits under the details pane in the third
/// column, shown while the record has a map; the pointer is followed by an input handler registered while the tab is
/// open, for its readout.
/// </remarks>
public sealed class MapTab(AreaCatalog areas, ContentReferenceIndex references, GameplayTagRegistry gameplayTags) : RecordBrowserTab<MapContentRecord, MapPreviewWindow>
{
    public const string TabTitle = "Map";
    public const string CellDetailsTitle = "Cell Details";

    private const float MapPreviewWidth = 720f;
    private const float CellDetailsGap = 8f;

    private readonly IReadOnlyList<MapContentRecord> _records = MapContentRecord.AllIn(areas);

    private WorkshopTabContext? _context;
    private CellDetailsWindow? _cellDetailsWindow;

    public override string Title => TabTitle;

    protected override string BrowserTitle => $"Map ({areas.Areas.Count} areas)";

    protected override string SearchGhostText => "Search areas";

    protected override string PreviewTitle => "Area";

    protected override float PreviewWidth => MapPreviewWidth;

    protected override IEnumerable<MapContentRecord> AllRecords => _records;

    protected override Guid IdOf(MapContentRecord record) => record.Id;

    protected override IReadOnlyList<RecordBrowserSection<MapContentRecord>> BuildSections(string search) =>
        MapBrowserSections.Build(_records, areas, search, gameplayTags);

    protected override RecordRowAppearance AppearanceOf(MapContentRecord record) => record.Kind switch
    {
        ContentRecordKind.Area => new(record.Name, null, "#", Color.LightGray),
        ContentRecordKind.AreaPalette => new(record.Name, null, "%", Color.LightGray),
        _ => new(record.Name, null, "+", Color.LightGray),
    };

    protected override string PreviewTitleFor(MapContentRecord record) => record.Kind switch
    {
        ContentRecordKind.Area => "Area",
        ContentRecordKind.AreaPalette => "Palette",
        _ => "Placement",
    };

    protected override string DetailsTitleFor(MapContentRecord record) => $"{PreviewTitleFor(record)} Details";

    protected override void ShowInPreview(MapPreviewWindow preview, MapContentRecord record, float contentWidth) =>
        preview.Configure(record, contentWidth);

    protected override string DetailsTitle => "Details";

    protected override Window? CreateDetailsWindow(ElementPoolService elementPool, ElementOptions options) =>
        elementPool.CreateElement<MapDetailsWindow>(null, options);

    protected override void ShowInDetails(Window details, MapContentRecord record, float contentWidth) =>
        ((MapDetailsWindow)details).Configure(record, contentWidth);

    protected override IReadOnlyList<RecordLinkSection> LinksOf(MapContentRecord record) =>
        ContentLinks.For(references, record.Kind, record.Id);

    protected override IReadOnlyList<WorkshopDependent> DependentsOf(MapContentRecord record) =>
        ContentLinks.DependentsOf(references, record.Kind, record.Id);

    protected override void OnOpened(WorkshopTabContext context)
    {
        _context = context;
        context.AddInputHandler(new PointerFollower(this));

        var cellDetails = context.UiEngine.ElementPoolService.CreateElement<CellDetailsWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = CellDetailsPosition(), MaximumSize = new Vector2(DetailsWidth, 1000f), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = CellDetailsTitle, ShowBorder = true, CanUserFocus = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        cellDetails.Configure(DetailsWidth);
        cellDetails.Initialize();
        context.Layers.Add(UiLayer.Base, cellDetails);
        _cellDetailsWindow = cellDetails;
        PreviewWindow.CellDetails = cellDetails;
        PreviewWindow.Details = DetailsWindow as MapDetailsWindow;
    }

    /// <summary>Cell Details is shown only for a record with a map: an area or a placement; the details pane, just rebuilt, is told what the preview shows.</summary>
    protected override void OnSelected(MapContentRecord record)
    {
        if (_cellDetailsWindow is { } cellDetails)
        {
            cellDetails.IsVisible = record.Kind != ContentRecordKind.AreaPalette;
        }

        PreviewWindow.PublishShownBuild();
    }

    /// <summary>Keeps Cell Details right under the details pane, however tall that grows.</summary>
    protected override void OnUpdate(GameTime gameTime)
    {
        if (_cellDetailsWindow is { } cellDetails && cellDetails.RelativePosition != CellDetailsPosition())
        {
            cellDetails.SetRelativePosition(CellDetailsPosition());
        }
    }

    /// <summary>The preview's map is as tall as the area leaves room for, so a new area builds it again.</summary>
    protected override void OnResized(Rectangle area) => PreviewWindow.Refit();

    protected override void OnClosing()
    {
        PreviewWindow.CellDetails = null;
        PreviewWindow.Details = null;
        if (_cellDetailsWindow is { } cellDetails && _context is { } context)
        {
            context.Layers.Remove(UiLayer.Base, cellDetails);
            cellDetails.Close();
        }

        _cellDetailsWindow = null;
    }

    private Vector2 CellDetailsPosition() =>
        DetailsWindow is { } details
            ? new Vector2(details.RelativePosition.X, details.RelativePosition.Y + details.CurrentSize.Y + CellDetailsGap)
            : Vector2.Zero;

    /// <summary>Hands the pointer to the preview every frame, for the hover readout.</summary>
    private sealed class PointerFollower(MapTab tab) : IUiInputHandler
    {
        public void OnPointerUpdate(Element? hoveredElement, Point position, bool isInteractionInProgress) =>
            tab.PreviewWindow.TrackPointer(hoveredElement, position);
    }
}
