using Microsoft.Xna.Framework;
using SpriteFormat;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Workshop.Sprites;

/// <summary>One spritesheet, zoomed, with the selected entry's grid and cells marked on it: a wall's face and top in their own colours.</summary>
/// <remarks>The wheel zooms (whole multiples, 1x to 16x) about the view's centre; a right-drag pans. The sheet is drawn as it is on disk -- a cell's turn, flip and tint are the previews'.</remarks>
public sealed class SpriteSheetView(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
    : Element(fontService, elementPoolService, renderService)
{
    private const int MaximumZoom = 16;
    private const int CellOutlineThickness = 2;

    private static readonly Color BackgroundColor = new(30, 30, 30);
    private static readonly Color GridColor = new Color(255, 255, 255) * 0.15f;
    private static readonly Color[] PartColors = [Color.Gold, Color.Cyan];

    private readonly List<(SpriteManifestCell Cell, Color Color)> _markedCells = [];

    private SpriteCellDrawer _drawer = null!;
    private string? _sheetPath;
    private Point _gridCellSize;
    private int _zoom = 1;
    private Vector2 _pan;
    private Vector2 _panAtDragStart;

    /// <summary>The zoom the sheet is drawn at: screen pixels per sheet pixel.</summary>
    public int Zoom => _zoom;

    protected override bool RequiresContentViewport => true;

    public void Configure(SpriteCellDrawer drawer) => _drawer = drawer;

    /// <summary>Shows sheetPath with entry's cells on it marked, zoomed to fit the view's width; nothing when either is null.</summary>
    public void Show(SpriteEntryView? entry, string? sheetPath)
    {
        _sheetPath = sheetPath;
        _markedCells.Clear();
        _gridCellSize = Point.Zero;
        _pan = Vector2.Zero;
        if (entry is null || sheetPath is null)
        {
            return;
        }

        for (var partIndex = 0; partIndex < entry.Parts.Count; partIndex++)
        {
            foreach (var cell in entry.Parts[partIndex].Part.Cells)
            {
                if (cell is not null && cell.SheetPath == sheetPath)
                {
                    _markedCells.Add((cell, PartColors[partIndex % PartColors.Length]));
                    if (_gridCellSize == Point.Zero)
                    {
                        _gridCellSize = new Point(cell.CellWidth, cell.CellHeight);
                    }
                }
            }
        }

        _zoom = _drawer.TextureOf(sheetPath) is { } texture ? Math.Clamp((int)(ContentSize.X / texture.Width), 1, MaximumZoom) : 1;
    }

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = Render.SpriteBatch;
        var target = new SpriteBatchDrawTarget(spriteBatch);
        var unitRectangle = Render.UnitRectangle;
        spriteBatch.Draw(unitRectangle, new Rectangle(0, 0, (int)ContentSize.X, (int)ContentSize.Y), BackgroundColor);

        if (_sheetPath is null || _drawer.TextureOf(_sheetPath) is not { } texture)
        {
            return;
        }

        var origin = new Point((int)_pan.X, (int)_pan.Y);
        spriteBatch.Draw(texture, new Rectangle(origin.X, origin.Y, texture.Width * _zoom, texture.Height * _zoom), Color.White);

        if (_gridCellSize.X > 0 && _gridCellSize.Y > 0)
        {
            for (var x = 0; x <= texture.Width; x += _gridCellSize.X)
            {
                spriteBatch.Draw(unitRectangle, new Rectangle(origin.X + x * _zoom, origin.Y, 1, texture.Height * _zoom), GridColor);
            }

            for (var y = 0; y <= texture.Height; y += _gridCellSize.Y)
            {
                spriteBatch.Draw(unitRectangle, new Rectangle(origin.X, origin.Y + y * _zoom, texture.Width * _zoom, 1), GridColor);
            }
        }

        foreach (var (cell, color) in _markedCells)
        {
            var source = cell.SourceRectangle;
            SpriteCellDrawer.DrawOutline(target, unitRectangle, new Rectangle(origin.X + source.X * _zoom, origin.Y + source.Y * _zoom, source.Width * _zoom, source.Height * _zoom), color, CellOutlineThickness);
        }
    }

    protected override bool OnMouseWheelAction(int notches)
    {
        var zoom = Math.Clamp(_zoom + Math.Sign(notches), 1, MaximumZoom);
        if (zoom != _zoom)
        {
            var centre = ContentSize / 2f;
            _pan = centre - (centre - _pan) * zoom / _zoom;
            _zoom = zoom;
        }

        return true;
    }

    protected override void OnRightDragStartAction() => _panAtDragStart = _pan;

    protected override void OnRightDragAction(Vector2 totalPixelDeltaSinceStart) => _pan = _panAtDragStart + totalPixelDeltaSinceStart;
}
