using Game.Areas;
using Game.Bootstrap;
using Game.Floors;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Presentation.UI.Details;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Builders.MapAreas;

/// <summary>The Map tab's preview, drawn by AreaMapView with its buttons and hover readout: an area as the game builds it, or -- for a fixed placement -- a starting-window neighborhood as a seeded world generates it.</summary>
/// <remarks>
/// <para>
/// The first row of buttons switches the layer shown; the second zooms, and for an area toggles the grid and rerolls the
/// preview instance's seed (which changes only what Chance and OneOf rolled). A placement's preview has a world seed and
/// Build, the starting window as a 3x3 grid of cells (its own by default; pressing one builds it, the one shown is
/// highlighted), and arrows in the 8 directions around the map that build the neighborhood that way (refused past the
/// starting window). A neighborhood is built on a worker, with a spinner over the map until it
/// is done; the generated one is TestMapBuilder.Plan for the record that seed gives it, every stamped area outlined and
/// named on hover. A palette has no map of its own.
/// </para>
/// <para>
/// The map is as tall as the window's maximum height leaves room for, so the window never reaches past the tab area: a
/// window that wraps its content ignores its maximum size, so it is built, measured, and built again with the map shrunk
/// by what it overflowed. Builds come from the shared AreaPreviewCache. Every row is built against the width Configure
/// is given.
/// </para>
/// </remarks>
public sealed class MapPreviewWindow(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer, AreaPreviewCache previews, GameCatalogs catalogs)
    : Window(fontService, elementPoolService, labelRenderer)
{
    public const int DefaultWorldSeed = 1;

    private const float ToolbarHeight = 26f;
    private const float DefaultMapViewHeight = 600f;
    private const float MinimumMapViewHeight = 160f;
    private const float ArrowSize = 24f;
    private const float NeighborhoodCellWidth = 72f;
    private const float PlacementControlsWidth = 300f;
    private const float ControlColumnGap = 16f;
    private const float RowHeight = ActivatableDetailsSections.RowHeight;

    private static readonly (MapLayer Layer, string Label)[] LayerButtons = [(MapLayer.UnderGround, "Under"), (MapLayer.Ground, "Ground"), (MapLayer.Flying, "Flying")];

    /// <summary>Each arrow's direction and glyph, in the order the frame lays them out.</summary>
    private static readonly (int DeltaX, int DeltaY, string Glyph)[] Arrows =
    [
        (-1, -1, "↖"), (0, -1, "↑"), (1, -1, "↗"),
        (-1, 0, "←"), (1, 0, "→"),
        (-1, 1, "↙"), (0, 1, "↓"), (1, 1, "↘"),
    ];

    private readonly ActivatableDetailsSections _sections = new(elementPoolService, fontService, catalogs.ActionCatalog, catalogs.GameplayTags);
    private readonly Dictionary<Guid, ulong> _seedsByArea = [];
    private readonly List<(MapLayer Layer, Button Button)> _layerButtons = [];
    private readonly List<(int DeltaX, int DeltaY, Button Button)> _arrowButtons = [];
    private readonly List<((int CellX, int CellY) Cell, Button Button)> _neighborhoodCellButtons = [];
    private readonly List<(NeighborhoodPreviewKey Key, Task<NeighborhoodPreviewBuild> Build)> _neighborhoodBuildsAwaited = [];
    private readonly IReadOnlyList<(int CellX, int CellY)> _windowNeighborhoods = FloorBuilder.StartingWindowNeighborhoods();

    private MapContentRecord? _record;
    private float _contentWidth;
    private bool _childrenReady;
    private float _mapViewHeight = DefaultMapViewHeight;
    private AreaMapView? _mapView;
    private Button? _gridButton;
    private TextWindow? _seedLine;
    private NumericField? _worldSeedField;
    private (int CellX, int CellY)? _shownNeighborhoodCell;
    private int? _shownSpawnCount;
    private MapLayer _layer = MapLayer.Ground;
    private bool _showsGrid = true;
    private int _worldSeed = DefaultWorldSeed;
    private (int CellX, int CellY)? _neighborhoodCell;
    private NeighborhoodPreviewKey? _requestedNeighborhood;

    /// <summary>Where the hover readout is shown -- the tab's Cell Details panel.</summary>
    public CellDetailsWindow? CellDetails { get; set; }

    /// <summary>The tab's details pane, told how many spawns the neighborhood shown planned.</summary>
    public MapDetailsWindow? Details { get; set; }

    /// <summary>The map view shown; null for a palette.</summary>
    public AreaMapView? MapView => _mapView;

    /// <summary>Shows record with every row built to contentWidth -- safe before the first Initialize (the build waits for it) and after (rebuilds at once).</summary>
    public void Configure(MapContentRecord record, float contentWidth)
    {
        if (!ReferenceEquals(record, _record))
        {
            _neighborhoodCell = record.Placement is { } placement ? CellInWindow(placement.NeighborhoodX, placement.NeighborhoodY) : null;
            _shownNeighborhoodCell = null;
            _shownSpawnCount = null;
        }

        _record = record;
        _contentWidth = contentWidth;

        if (_childrenReady)
        {
            Rebuild();
        }
    }

    /// <summary>Builds the shown record again to the window's current maximum height -- the tab's area changed.</summary>
    public void Refit()
    {
        if (_childrenReady)
        {
            Rebuild();
        }
    }

    /// <summary>Follows the pointer: over the map view, the tile under it; anywhere else, none.</summary>
    public void TrackPointer(Element? hoveredElement, Point position)
    {
        if (_mapView is not { } mapView)
        {
            return;
        }

        if (ReferenceEquals(hoveredElement, mapView))
        {
            mapView.Hover(position);
            return;
        }

        mapView.ClearHover();
    }

    /// <summary>Takes each worker build that has finished: kept in the cache, and shown when it is the neighborhood asked for last.</summary>
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        for (var index = _neighborhoodBuildsAwaited.Count - 1; index >= 0; index--)
        {
            var (key, build) = _neighborhoodBuildsAwaited[index];
            if (!build.IsCompleted)
            {
                continue;
            }

            _neighborhoodBuildsAwaited.RemoveAt(index);
            if (!build.IsCompletedSuccessfully)
            {
                previews.ForgetFailedNeighborhood(key);
                if (key == _requestedNeighborhood)
                {
                    StopLoading($"Couldn't build ({key.CellX}, {key.CellY}) of seed {key.WorldSeed}: {build.Exception?.GetBaseException().Message}");
                }

                continue;
            }

            previews.KeepNeighborhood(key, build.Result);
            if (key == _requestedNeighborhood)
            {
                ShowNeighborhood(build.Result);
            }
        }
    }

    protected override void OnChildrenInitialized()
    {
        base.OnChildrenInitialized();
        _childrenReady = true;

        Rebuild();
    }

    /// <summary>Builds the rows, then again with the map shrunk by however far the first build reached past the maximum height, then shows the scene.</summary>
    private void Rebuild()
    {
        _mapViewHeight = DefaultMapViewHeight;
        BuildRows();
        var overflow = CurrentSize.Y - MaximumSize.Y;
        if (_mapView is not null && overflow > 0)
        {
            _mapViewHeight = System.Math.Max(MinimumMapViewHeight, _mapViewHeight - overflow);
            BuildRows();
        }

        ShowScene();
    }

    private void BuildRows()
    {
        ElementPoolService.CloseAllChildren(this);
        _layerButtons.Clear();
        _arrowButtons.Clear();
        _mapView = null;
        _gridButton = null;
        _seedLine = null;
        _worldSeedField = null;
        _neighborhoodCellButtons.Clear();

        if (_record is null)
        {
            return;
        }

        var width = _contentWidth - ContentPadding.X * 2;
        if (_record.Area is { } area)
        {
            BuildArea(area, width);
            return;
        }

        if (_record.Placement is { } placement)
        {
            BuildNeighborhood(placement, width);
            return;
        }

        _sections.BuildNameRow(this, null, "%", Color.LightGray, _record.Name, width);
        CreateLine(width, "A palette is drawn in the areas that use it: select one to see it.", ActivatableDetailsSections.BodyTextColor);
    }

    private void ShowScene()
    {
        if (_record?.Area is { } area)
        {
            _mapView?.Show(previews.Get(area, SeedOf(area)));
            ShowSeed(area);
            return;
        }

        if (_record?.Placement is not null)
        {
            RequestNeighborhood();
        }
    }

    private void BuildArea(AreaDefinition area, float width)
    {
        _sections.BuildNameRow(this, null, "#", Color.LightGray, area.Name, width);
        BuildLayerRow(this, width);

        var toolbar = AddToolbarRow(this, width);
        AddZoomButtons(toolbar);
        _gridButton = AddButton(toolbar, "Grid", 56f, ToggleGrid);
        _gridButton.IsHighlighted = _showsGrid;
        AddButton(toolbar, "Reroll", 66f, () => Reroll(area));
        _seedLine = CreateLine(width, string.Empty, ActivatableDetailsSections.BodyTextColor);

        _mapView = CreateMapView(this, Vector2.Zero, new Vector2(width, _mapViewHeight));
        AddChild(_mapView);
        ShowReadout(null);
    }

    private void BuildNeighborhood(AreaPlacement placement, float width)
    {
        _sections.BuildNameRow(this, null, "+", Color.LightGray, $"Neighborhood around {MapContentRecord.PlacementName(placement, catalogs.Areas)}", width);
        AddControlColumns(width, out var left, out var right);
        BuildLayerRow(left, PlacementControlsWidth);
        AddZoomButtons(AddToolbarRow(left, PlacementControlsWidth));

        var generation = AddToolbarRow(left, PlacementControlsWidth);
        AddLabel(generation, "World seed", 80f);
        _worldSeedField = AddField<NumericField>(generation, 120f, Color.White);
        _worldSeedField.Configure(_worldSeed, int.MinValue, int.MaxValue);
        _worldSeedField.SetValue(_worldSeed);
        _worldSeedField.TextSubmitted += _ => RequestNeighborhood();
        AddButton(generation, "Build", 60f, RequestNeighborhood);
        BuildNeighborhoodGrid(right);

        AddChild(BuildArrowFrame(width));
        ShowReadout(null);
    }

    /// <summary>The starting window as a 3x3 grid of its cells' coordinates, north at the top; pressing one builds it, and the one shown is highlighted.</summary>
    private void BuildNeighborhoodGrid(Window column)
    {
        var rows = _windowNeighborhoods.GroupBy(cell => cell.CellY).OrderBy(row => row.Key).ToList();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = AddToolbarRow(column, NeighborhoodCellWidth * rows[rowIndex].Count());
            foreach (var cell in rows[rowIndex].OrderBy(cell => cell.CellX))
            {
                var button = AddButton(row, $"({cell.CellX}, {cell.CellY})", NeighborhoodCellWidth, () => SelectNeighborhoodCell(cell));
                _neighborhoodCellButtons.Add((cell, button));
            }
        }

        RefreshNeighborhoodGrid();
    }

    /// <summary>The placement's controls as two columns three rows tall, side by side: left the layer, zoom and seed rows, right the neighborhood grid.</summary>
    private void AddControlColumns(float width, out Window left, out Window right)
    {
        var height = ToolbarHeight * 3;
        var controls = AddPanel(this, new Vector2(width, height), ChildElementTileMode.Horizontal);
        left = AddPanel(controls, new Vector2(PlacementControlsWidth + ControlColumnGap, height), ChildElementTileMode.Vertical);
        right = AddPanel(controls, new Vector2(NeighborhoodCellWidth * 3, height), ChildElementTileMode.Vertical);
    }

    private Window AddPanel(Window host, Vector2 size, ChildElementTileMode tileMode)
    {
        var panel = ElementPoolService.CreateElement<Window>(host, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = tileMode },
            Layout = new ElementLayoutOptions { Size = size, MinimumSize = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        panel.ContentPadding = Vector2.Zero;
        host.AddChild(panel);
        return panel;
    }

    private void SelectNeighborhoodCell((int CellX, int CellY) cell)
    {
        _neighborhoodCell = cell;
        RequestNeighborhood();
    }

    private void RefreshNeighborhoodGrid()
    {
        foreach (var (cell, button) in _neighborhoodCellButtons)
        {
            button.IsHighlighted = cell == _shownNeighborhoodCell;
        }
    }

    /// <summary>The map view with an arrow on each of its 8 sides and corners, outside it: corners square, sides as long as the map.</summary>
    private Window BuildArrowFrame(float width)
    {
        var frameSize = new Vector2(width, _mapViewHeight + ArrowSize * 2);
        var frame = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Floating },
            Layout = new ElementLayoutOptions { Size = frameSize, MinimumSize = frameSize, MaximumSize = frameSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        frame.ContentPadding = Vector2.Zero;

        var mapSize = new Vector2(width - ArrowSize * 2, _mapViewHeight);
        _mapView = CreateMapView(frame, new Vector2(ArrowSize, ArrowSize), mapSize);
        frame.AddChild(_mapView);

        foreach (var (deltaX, deltaY, glyph) in Arrows)
        {
            var position = new Vector2(
                deltaX < 0 ? 0 : deltaX == 0 ? ArrowSize : ArrowSize + mapSize.X,
                deltaY < 0 ? 0 : deltaY == 0 ? ArrowSize : ArrowSize + mapSize.Y);
            var size = new Vector2(deltaX == 0 ? mapSize.X : ArrowSize, deltaY == 0 ? mapSize.Y : ArrowSize);
            var button = ElementPoolService.CreateElement<Button>(frame, new ElementOptions
            {
                Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, MinimumSize = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = true },
            });
            button.LeftText = glyph;
            var (capturedDeltaX, capturedDeltaY) = (deltaX, deltaY);
            button.Clicked += _ => Navigate(capturedDeltaX, capturedDeltaY);
            frame.AddChild(button);
            _arrowButtons.Add((deltaX, deltaY, button));
        }

        return frame;
    }

    /// <summary>Builds the neighborhood next to the shown one in that direction -- refused past the starting window, whose records are the only exact ones.</summary>
    private void Navigate(int deltaX, int deltaY)
    {
        var (cellX, cellY) = _neighborhoodCell ?? (0, 0);
        if (CellInWindow(cellX + deltaX, cellY + deltaY) is not { } next)
        {
            return;
        }

        _neighborhoodCell = next;
        RequestNeighborhood();
    }

    /// <summary>Shows the neighborhood the fields name: at once when it is kept, otherwise built on a worker with the spinner over the map until it is done.</summary>
    /// <remarks>Takes what is typed in the seed box first: a press on Build or an arrow doesn't move focus away from it.</remarks>
    private void RequestNeighborhood()
    {
        if (_mapView is not { } mapView || _worldSeedField is not { } seedField)
        {
            return;
        }

        seedField.CommitTypedText();
        _worldSeed = (int)seedField.Value;
        var (cellX, cellY) = _neighborhoodCell ?? (0, 0);
        var key = new NeighborhoodPreviewKey(_worldSeed, cellX, cellY);
        _requestedNeighborhood = key;
        RefreshArrows();

        if (previews.TryGetNeighborhood(key, out var kept))
        {
            ShowNeighborhood(kept);
            return;
        }

        if (!_neighborhoodBuildsAwaited.Any(awaited => awaited.Key == key))
        {
            _neighborhoodBuildsAwaited.Add((key, previews.BuildNeighborhoodAsync(key)));
        }

        var loadingText = $"Building neighborhood ({cellX}, {cellY}) of seed {_worldSeed}";
        mapView.MessageText = null;
        mapView.LoadingText = loadingText;
    }

    private void ShowNeighborhood(NeighborhoodPreviewBuild build)
    {
        if (_mapView is not { } mapView)
        {
            return;
        }

        mapView.LoadingText = null;
        mapView.MessageText = null;
        mapView.Show(build, build.StartingFocus());
        _shownNeighborhoodCell = (build.Record.CellX, build.Record.CellY);
        RefreshNeighborhoodGrid();
        _shownSpawnCount = build.Plan.Spawns.Count;
        PublishShownBuild();
        ShowReadout(null);
    }

    private void StopLoading(string message)
    {
        if (_mapView is { } mapView)
        {
            mapView.LoadingText = null;
            mapView.MessageText = message;
        }
    }

    /// <summary>Tells the details pane what the neighborhood shown planned -- again after the pane is rebuilt for a new selection.</summary>
    public void PublishShownBuild() => Details?.ShowSpawnCount(_shownSpawnCount);

    /// <summary>An arrow is enabled while the neighborhood that way is in the starting window.</summary>
    private void RefreshArrows()
    {
        var (cellX, cellY) = _neighborhoodCell ?? (0, 0);
        foreach (var (deltaX, deltaY, button) in _arrowButtons)
        {
            button.Enabled = CellInWindow(cellX + deltaX, cellY + deltaY) is not null;
        }
    }

    private (int CellX, int CellY)? CellInWindow(int cellX, int cellY) =>
        _windowNeighborhoods.Contains((cellX, cellY)) ? (cellX, cellY) : null;

    private void BuildLayerRow(Window host, float width)
    {
        var layerRow = AddToolbarRow(host, width);
        foreach (var (layer, label) in LayerButtons)
        {
            var button = AddButton(layerRow, label, 70f, () => SelectLayer(layer));
            button.IsHighlighted = layer == _layer;
            _layerButtons.Add((layer, button));
        }
    }

    private void AddZoomButtons(Window toolbar)
    {
        AddButton(toolbar, "Zoom in", 70f, () => _mapView?.CycleZoom(-1));
        AddButton(toolbar, "Zoom out", 76f, () => _mapView?.CycleZoom(1));
    }

    private AreaMapView CreateMapView(Element parent, Vector2 position, Vector2 size)
    {
        var mapView = ElementPoolService.CreateElement<AreaMapView>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, MinimumSize = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Black },
        });
        mapView.Clear();
        mapView.SetLayer(_layer);
        mapView.ShowsGrid = _showsGrid;
        mapView.HoveredPositionChanged += ShowReadout;
        return mapView;
    }

    private Window AddToolbarRow(Window host, float width)
    {
        var row = ElementPoolService.CreateElement<Window>(host, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Horizontal },
            Layout = new ElementLayoutOptions { Size = new Vector2(width, ToolbarHeight), MinimumSize = new Vector2(width, ToolbarHeight), MaximumSize = new Vector2(width, ToolbarHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        row.ContentPadding = Vector2.Zero;
        host.AddChild(row);
        return row;
    }

    private Button AddButton(Window toolbar, string text, float buttonWidth, Action onClick)
    {
        var size = new Vector2(buttonWidth, ToolbarHeight);
        var button = ElementPoolService.CreateElement<Button>(toolbar, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = size, MinimumSize = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
        });
        button.LeftText = text;
        button.Clicked += _ => onClick();
        toolbar.AddChild(button);
        return button;
    }

    private T AddField<T>(Window toolbar, float fieldWidth, Color textColor)
        where T : Element
    {
        var size = new Vector2(fieldWidth, ToolbarHeight);
        var field = ElementPoolService.CreateElement<T>(toolbar, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = size, MinimumSize = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
            Text = new TextOptions { TextColor = textColor },
        });
        toolbar.AddChild(field);
        return field;
    }

    private void AddLabel(Window toolbar, string text, float labelWidth)
    {
        var size = new Vector2(labelWidth, ToolbarHeight);
        var label = ElementPoolService.CreateElement<TextWindow>(toolbar, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = size, MinimumSize = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Text = new TextOptions { Text = text, TextColor = ActivatableDetailsSections.BodyTextColor },
        });
        label.ContentFont = _sections.BodyFont;
        toolbar.AddChild(label);
    }

    private void SelectLayer(MapLayer layer)
    {
        _layer = layer;
        _mapView?.SetLayer(layer);
        foreach (var (buttonLayer, button) in _layerButtons)
        {
            button.IsHighlighted = buttonLayer == layer;
        }

        ShowReadout(_mapView?.HoveredPosition);
    }

    private void ToggleGrid()
    {
        _showsGrid = !_showsGrid;
        if (_mapView is { } mapView)
        {
            mapView.ShowsGrid = _showsGrid;
        }

        if (_gridButton is { } gridButton)
        {
            gridButton.IsHighlighted = _showsGrid;
        }
    }

    private void Reroll(AreaDefinition area)
    {
        _seedsByArea[area.Id] = SeedOf(area) + 1;
        _mapView?.Show(previews.Get(area, SeedOf(area)));
        ShowSeed(area);
        ShowReadout(null);
    }

    private ulong SeedOf(AreaDefinition area) => _seedsByArea.GetValueOrDefault(area.Id);

    private void ShowSeed(AreaDefinition area) => _seedLine?.UpdateText($"Preview seed {SeedOf(area)} (Reroll for other Chance and OneOf rolls)");

    private void ShowReadout(Point? position)
    {
        var lines = position is { } hovered && _mapView?.Scene is { } scene
            ? AreaCellReadout.Describe(scene, hovered.X, hovered.Y, _layer)
            : AreaCellReadout.Nothing;
        CellDetails?.ShowLines(lines);
    }

    private TextWindow CreateLine(float width, string text, Color color)
    {
        var line = ElementPoolService.CreateElement<TextWindow>(this, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(width, RowHeight), MinimumSize = new Vector2(width, RowHeight), MaximumSize = new Vector2(width, RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = color },
        });
        line.ContentFont = _sections.BodyFont;
        AddChild(line);
        return line;
    }
}
