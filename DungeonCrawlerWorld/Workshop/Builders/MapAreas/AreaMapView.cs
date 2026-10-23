using Engine.Math;
using FontStashSharp;
using Game.Areas;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.MapRendering;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Builders.MapAreas;

/// <summary>A preview build drawn by the game's own MapRenderer, one layer at a time: one area with a grid and its transparent cells hatched, or a neighborhood with every stamped area outlined and named; markers as labelled badges and the hovered cell outlined.</summary>
/// <remarks>
/// The wheel zooms (MapCamera's levels) and a right-drag pans, as on the game's map. The overlays are drawn after the
/// renderer, in the element's own content coordinates (its viewport). An area's cell is transparent on a layer when its
/// symbol is a space, or when the area has no rows for that layer at all. While LoadingText is set, a spinner is drawn
/// over whatever is shown.
/// </remarks>
public sealed class AreaMapView(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService, SpriteSheetService spriteSheetService)
    : Element(fontService, elementPoolService, renderService)
{
    private const int MarkerLabelFontSize = 11;
    private const int AreaLabelFontSize = 14;
    private const int HatchSpacingPixels = 6;
    private const int HatchDotPixels = 2;
    private const int AreaOutlinePixels = 2;
    private const float MarkerBadgeFraction = 0.5f;
    private const int SpinnerDotCount = 8;
    private const int SpinnerDotPixels = 6;
    private const float SpinnerRadius = 18f;
    private const float SpinnerStepsPerSecond = 10f;
    private const float SpinnerTrailFade = 0.85f;

    private static readonly Color GridColor = new(0, 0, 0, 90);
    private static readonly Color HatchColor = new(255, 255, 255, 70);
    private static readonly Color MarkerColor = Color.Gold;
    private static readonly Color MarkerLabelColor = Color.White;
    private static readonly Color AreaOutlineColor = Color.Gold;
    private static readonly Color HoveredAreaOutlineColor = Color.White;
    private static readonly Color HoverColor = Color.White;
    private static readonly Color LoadingDimColor = new(0, 0, 0, 150);
    private static readonly IReadOnlyList<int> NoWatchedEntities = [];

    private readonly SpriteFontBase _markerFont = fontService.GetFont(MarkerLabelFontSize);
    private readonly SpriteFontBase _areaLabelFont = fontService.GetFont(AreaLabelFontSize);

    private IMapPreviewScene? _scene;
    private MapCamera? _camera;
    private MapRenderer? _renderer;
    private Point? _hoveredPosition;

    /// <summary>The MapLayer shown.</summary>
    public MapLayer Layer { get; private set; } = MapLayer.Ground;

    /// <summary>Whether a shown area's grid is drawn; a neighborhood draws none.</summary>
    public bool ShowsGrid { get; set; } = true;

    /// <summary>The map tile under the pointer, or null when it is off the scene's area (or off the map, for a neighborhood).</summary>
    public Point? HoveredPosition => _hoveredPosition;

    public IMapPreviewScene? Scene => _scene;

    public ZoomLevel? Zoom => _camera?.CurrentZoomLevel;

    /// <summary>While set, a spinner and this text are drawn over the view: a build is running.</summary>
    public string? LoadingText { get; set; }

    /// <summary>While set (and nothing is loading), this text is drawn over the view, dimmed: why there is nothing to show.</summary>
    public string? MessageText { get; set; }

    /// <summary>The hovered tile changed, to a tile or to none.</summary>
    public event Action<Point?>? HoveredPositionChanged;

    protected override bool RequiresContentViewport => true;

    /// <summary>Shows an area at the closest zoom that fits it whole, or a neighborhood at the farthest zoom centred on focus.</summary>
    public void Show(IMapPreviewScene scene, Vector3Int? focus = null)
    {
        _renderer?.Dispose();
        _scene = scene;
        var views = scene.Session.Views;
        _camera = new MapCamera(views.MapView);
        _camera.Initialize(ContentSize);
        _renderer = new MapRenderer(views.MapView, views.AuraPower, _camera, Render, FontService, spriteSheetService);

        if (scene.Area is { } instance)
        {
            foreach (var zoom in Enum.GetValues<ZoomLevel>())
            {
                _camera.UpdateZoomLevel(zoom, ContentSize);
                if (_camera.TileColumns >= instance.Definition.Width && _camera.TileRows >= instance.Definition.Height)
                {
                    break;
                }
            }
        }
        else
        {
            _camera.UpdateZoomLevel(ZoomLevel.Borough, ContentSize);
        }

        if (focus is { } focusPosition)
        {
            _camera.CenterCameraOn(focusPosition);
        }

        SetHoveredPosition(null);
    }

    /// <summary>Shows nothing -- a pooled view never keeps the scene or loading text of its last use.</summary>
    public void Clear()
    {
        _scene = null;
        _camera = null;
        _renderer?.Dispose();
        _renderer = null;
        LoadingText = null;
        MessageText = null;
        SetHoveredPosition(null);
    }

    public void SetLayer(MapLayer layer) => Layer = layer;

    /// <summary>Back to the pool: the renderer's render targets are released and nothing of this use is kept.</summary>
    protected override void OnClosed()
    {
        base.OnClosed();
        Clear();
    }

    /// <summary>Zooms in (negative direction) or out (positive), as the game's map does.</summary>
    public void CycleZoom(int direction) => _camera?.CycleZoom(direction, ContentSize);

    /// <summary>Follows the pointer at screen position: the tile under it, or none.</summary>
    public void Hover(Point position)
    {
        if (_camera is not { } camera || _scene is not { } scene || !camera.TryGetHoveredMapPosition(position, ContentAbsolutePosition, out var mapPosition))
        {
            SetHoveredPosition(null);
            return;
        }

        var isOnScene = scene.Area is { } instance
            ? instance.Contains(mapPosition.X, mapPosition.Y)
            : scene.Session.World.IsOnMap(new Vector3Int(mapPosition.X, mapPosition.Y, (int)Layer));
        SetHoveredPosition(isOnScene ? mapPosition : null);
    }

    public void ClearHover() => SetHoveredPosition(null);

    /// <summary>The stamped instance whose box holds the tile, if any.</summary>
    public AreaInstance? InstanceAt(Point position) =>
        _scene?.Stamp.Instances.FirstOrDefault(instance => instance.Contains(position.X, position.Y));

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        _renderer?.Update((int)Layer);
    }

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = Render.SpriteBatch;
        var unitRectangle = Render.UnitRectangle;
        if (_renderer is { } renderer && _camera is { } camera && _scene is { } scene)
        {
            DrawScene(spriteBatch, unitRectangle, renderer, camera, scene);
        }

        if (LoadingText is { } loadingText)
        {
            DrawLoading(spriteBatch, unitRectangle, loadingText, gameTime);
        }
        else if (MessageText is { } messageText)
        {
            DrawMessage(spriteBatch, unitRectangle, messageText);
        }
    }

    protected override bool OnMouseWheelAction(int notches)
    {
        CycleZoom(notches > 0 ? -1 : 1);
        return true;
    }

    protected override void OnRightDragStartAction() => _camera?.BeginDrag();

    protected override void OnRightDragAction(Vector2 totalPixelDeltaSinceStart)
    {
        if (totalPixelDeltaSinceStart != Vector2.Zero)
        {
            _camera?.ApplyDrag(totalPixelDeltaSinceStart);
        }
    }

    protected override void OnRightDragEndAction() => _camera?.EndDrag();

    private void DrawScene(SpriteBatch spriteBatch, Texture2D unitRectangle, MapRenderer renderer, MapCamera camera, IMapPreviewScene scene)
    {
        renderer.Draw((int)Layer, NoWatchedEntities);

        if (scene.Area is { } area)
        {
            DrawAreaCells(spriteBatch, unitRectangle, camera, area);
        }
        else
        {
            DrawInstanceOutlines(spriteBatch, unitRectangle, camera, scene.Stamp);
        }

        foreach (var marker in scene.Stamp.Markers)
        {
            if (marker.Position.Z == (int)Layer && camera.TryGetTileRectangle(marker.Position.X, marker.Position.Y, out var tile))
            {
                DrawMarker(spriteBatch, unitRectangle, tile, marker.Marker.MarkerId);
            }
        }

        if (_hoveredPosition is { } hovered && camera.TryGetTileRectangle(hovered.X, hovered.Y, out var hoveredTile))
        {
            DrawOutline(spriteBatch, unitRectangle, hoveredTile, HoverColor, 1);
        }
    }

    /// <summary>A dimmed view with a ring of dots turning about its centre, and the text under it.</summary>
    private void DrawLoading(SpriteBatch spriteBatch, Texture2D unitRectangle, string text, GameTime gameTime)
    {
        spriteBatch.Draw(unitRectangle, new Rectangle(0, 0, (int)ContentSize.X, (int)ContentSize.Y), LoadingDimColor);
        var centre = ContentSize / 2;
        var lit = (int)(gameTime.TotalGameTime.TotalSeconds * SpinnerStepsPerSecond) % SpinnerDotCount;
        for (var dot = 0; dot < SpinnerDotCount; dot++)
        {
            var angle = dot * MathHelper.TwoPi / SpinnerDotCount;
            var position = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * SpinnerRadius;
            var stepsBehind = (lit - dot + SpinnerDotCount) % SpinnerDotCount;
            var brightness = 1f - stepsBehind / (float)SpinnerDotCount * SpinnerTrailFade;
            spriteBatch.Draw(unitRectangle, new Rectangle((int)position.X - SpinnerDotPixels / 2, (int)position.Y - SpinnerDotPixels / 2, SpinnerDotPixels, SpinnerDotPixels), Color.White * brightness);
        }

        var textSize = _areaLabelFont.MeasureString(text);
        LabelRenderer.Draw(spriteBatch, _areaLabelFont, text, new Vector2(centre.X - textSize.X / 2, centre.Y + SpinnerRadius + SpinnerDotPixels * 2), Color.White, outline: true);
    }

    private void DrawMessage(SpriteBatch spriteBatch, Texture2D unitRectangle, string text)
    {
        spriteBatch.Draw(unitRectangle, new Rectangle(0, 0, (int)ContentSize.X, (int)ContentSize.Y), LoadingDimColor);
        var textSize = _areaLabelFont.MeasureString(text);
        LabelRenderer.Draw(spriteBatch, _areaLabelFont, text, (ContentSize - textSize) / 2, Color.White, outline: true);
    }

    private void SetHoveredPosition(Point? position)
    {
        if (position == _hoveredPosition)
        {
            return;
        }

        _hoveredPosition = position;
        HoveredPositionChanged?.Invoke(position);
    }

    private void DrawAreaCells(SpriteBatch spriteBatch, Texture2D unitRectangle, MapCamera camera, AreaInstance instance)
    {
        var area = instance.Definition;
        var rows = area.Layers.RowsOf(Layer);
        for (var y = 0; y < area.Height; y++)
        {
            for (var x = 0; x < area.Width; x++)
            {
                if (!camera.TryGetTileRectangle(instance.OriginX + x, instance.OriginY + y, out var tile))
                {
                    continue;
                }

                if (rows is null || rows[y][x] == AreaDefinition.TransparentSymbol)
                {
                    DrawHatching(spriteBatch, unitRectangle, tile);
                }

                if (ShowsGrid)
                {
                    DrawOutline(spriteBatch, unitRectangle, tile, GridColor, 1);
                }
            }
        }
    }

    /// <summary>Each stamped instance's box, its name inside its top-left corner; the hovered one brighter.</summary>
    private void DrawInstanceOutlines(SpriteBatch spriteBatch, Texture2D unitRectangle, MapCamera camera, AreaStampResult stamp)
    {
        var hoveredInstance = _hoveredPosition is { } hovered ? InstanceAt(hovered) : null;
        var tileSize = camera.CurrentTileSize;
        foreach (var instance in stamp.Instances)
        {
            var topLeft = camera.TileToScreen(new Vector2(instance.OriginX, instance.OriginY));
            var box = new Rectangle((int)topLeft.X, (int)topLeft.Y, instance.Definition.Width * tileSize.X, instance.Definition.Height * tileSize.Y);
            if (!box.Intersects(new Rectangle(0, 0, (int)ContentSize.X, (int)ContentSize.Y)))
            {
                continue;
            }

            var color = ReferenceEquals(instance, hoveredInstance) ? HoveredAreaOutlineColor : AreaOutlineColor;
            DrawOutline(spriteBatch, unitRectangle, box, color, AreaOutlinePixels);
            LabelRenderer.Draw(spriteBatch, _areaLabelFont, instance.Definition.Name, new Vector2(box.X + AreaOutlinePixels + 2, box.Y + AreaOutlinePixels + 2), color, outline: true);
        }
    }

    /// <summary>Diagonal rows of dots across the tile: reads as hatching over any terrain.</summary>
    private static void DrawHatching(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle tile)
    {
        for (var offset = -tile.Height; offset < tile.Width; offset += HatchSpacingPixels)
        {
            for (var step = 0; step < tile.Height; step += HatchDotPixels * 2)
            {
                var x = tile.X + offset + step;
                if (x >= tile.X && x + HatchDotPixels <= tile.Right)
                {
                    spriteBatch.Draw(unitRectangle, new Rectangle(x, tile.Y + step, HatchDotPixels, HatchDotPixels), HatchColor);
                }
            }
        }
    }

    private void DrawMarker(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle tile, string label)
    {
        var size = System.Math.Max((int)(tile.Width * MarkerBadgeFraction), 3);
        var badge = new Rectangle(tile.Center.X - size / 2, tile.Center.Y - size / 2, size, size);
        spriteBatch.Draw(unitRectangle, badge, MarkerColor * 0.8f);
        DrawOutline(spriteBatch, unitRectangle, badge, Color.Black, 1);
        LabelRenderer.Draw(spriteBatch, _markerFont, label, new Vector2(tile.X, tile.Bottom), MarkerLabelColor, outline: true);
    }

    private static void DrawOutline(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle rectangle, Color color, int thickness)
    {
        spriteBatch.Draw(unitRectangle, new Rectangle(rectangle.X, rectangle.Y, rectangle.Width, thickness), color);
        spriteBatch.Draw(unitRectangle, new Rectangle(rectangle.X, rectangle.Bottom - thickness, rectangle.Width, thickness), color);
        spriteBatch.Draw(unitRectangle, new Rectangle(rectangle.X, rectangle.Y, thickness, rectangle.Height), color);
        spriteBatch.Draw(unitRectangle, new Rectangle(rectangle.Right - thickness, rectangle.Y, thickness, rectangle.Height), color);
    }
}
