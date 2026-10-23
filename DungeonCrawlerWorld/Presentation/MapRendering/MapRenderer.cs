using Engine.Diagnostics;
using Engine.Math;
using Engine.Tiles;
using FontStashSharp;
using Game.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using SpriteFormat;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.MapRendering;

/// <summary>Draws the map as the world looks through a camera: the flat fill, terrain and walls, and everything standing on them, lit by the auras' glow.</summary>
/// <remarks>
/// <para>
/// Reads the world only through IMapViewQuery and AuraPowerView, so the game feeds it a live session and a map editor an
/// authored layout. Draws nothing about what the player is doing (targeting, selection): that is drawn over it by its host.
/// </para>
/// <para>
/// Two calls a frame: Update, outside the UI's pass, re-renders the cached ground and wall-top images when they are
/// stale; Draw rebuilds the glow texture when it is stale and blits the images under the row walk. Each is stale when the
/// camera shows something other than what it was built for (MapImageKey), so no camera move has to announce itself. What
/// the key can't see is told: terrain changes through InvalidateTerrain/InvalidateTerrainIn, and aura changes through
/// AuraPowerView.Version, read here.
/// </para>
/// <para>
/// Draw order is significant, not incidental -- each pass lands on top of the previous one with no depth buffer
/// (SpriteEffectBatch draws in call order): the flat fill first so everything else has something to draw over,
/// then the ground (a terrain's sprite is its background; terrain with none shows the flat fill under its glyph), the
/// walls' tops, and the row walk. The glow is not a pass: the sprite effect lights each draw as it is made (see Draw).
/// </para>
/// </remarks>
public sealed class MapRenderer : IDisposable
{
    private const int MaxTinyEntitiesDrawn = 9;
    private const int TinyGridDimension = 3;
    private static readonly Color UpLayerBadgeColor = Color.Blue;
    private static readonly Color DownLayerBadgeColor = new(101, 67, 33);

    private const float HealthBarWidthFraction = 0.9f;
    private const int HealthBarHeightPixels = 4;

    /// <summary>Fraction of a single tile's own size (not the corpse's own possibly-multi-tile footprint) -- anchored to the tile's top-right corner the same way DrawLayerBadges' up/down arrows are.</summary>
    private const float LootBagBadgeSizeFraction = 0.4f;
    private const string LootBagSpriteName = "LootBag-Red";

    /// <summary>Color of a dodging entity's own inner-fade glow -- Combat Overhaul: Dodge.</summary>
    private static readonly Color DodgingGlowColor = Color.LightGreen;

    /// <summary>Boosts GlowRenderer's own 50%-at-the-edge default toward fully opaque -- see the call site's own doc comment for why the un-boosted default read as invisible.</summary>
    private const float DodgingGlowAlphaMultiplier = 2f;

    /// <summary>A Phasing occupant draws at half opacity over the Blocking one sharing its tiles, so it reads as translucently in front rather than hidden.</summary>
    private const float PhasingAlphaMultiplier = 0.5f;

    /// <summary>Fraction of a tile's own size the charging-action badge renders at, matching LootBagBadgeSizeFraction's own scale.</summary>
    private const float ChargingBadgeSizeFraction = 0.4f;

    private static readonly Color MapFillColor = new(40, 40, 40);

    /// <summary>What a tile off the loaded map is drawn as, so the edge of the world reads as an edge rather than as floor with nothing on it.</summary>
    private static readonly Color OffMapColor = Color.Black;

    private readonly IMapViewQuery _mapView;
    private readonly AuraPowerView _auraPower;
    private readonly MapCamera _camera;
    private readonly UiRenderService _renderService;
    private readonly SpriteSheetService _spriteSheetService;


    private readonly FontStashTextMeasurer _mediumFontMeasurer;
    private readonly FontStashTextMeasurer _largeFontMeasurer;
    private readonly FontStashTextMeasurer _hugeFontMeasurer;
    private readonly FontStashTextMeasurer _tinyFontMeasurer;
    private readonly FontStashTextMeasurer _badgeFontMeasurer;

    /// <summary>
    /// LootBag-Red resolved once, not per badge per frame -- a sprite lookup by name is a
    /// string-keyed dictionary lookup, and repeating it for every corpse badge of every frame is
    /// needless work on the draw path. The first cell rather than a random one because a badge has
    /// to look the same on every corpse.
    /// </summary>
    private readonly SpriteView? _lootBagSprite;

    /// <summary>What the occupant pass draws and in what order, re-planned every frame into the same buffers.</summary>
    private readonly MapDrawOrder _drawOrder = new();

    /// <summary>Which walls' faces and tops are cached and which the row walk draws live; re-planned whenever the cached terrain image is rendered (DrawStructures), so its overhangs always match the image.</summary>
    private readonly MapWallPlan _wallPlan = new();

    /// <summary>The terrain and structures around the grid, read once per rebuild of the cached terrain image (DrawTerrainDirectly) and resolved for drawing.</summary>
    private readonly MapTerrainCells _terrainCells;

    /// <summary>The drawn sprites of this frame's watched entities, which overhangs fade over (OverhangFade). A field so it isn't reallocated per frame.</summary>
    private readonly List<Rectangle> _watchedRectangles = [];

    /// <summary>The image passes, as delegates made once rather than per Update.</summary>
    private readonly Action<SpriteEffectBatch, MapImageKey> _drawGroundIntoCache;
    private readonly Action<SpriteEffectBatch, MapImageKey> _drawTopsIntoCache;

    /// <summary>Floors, off-map black and the cached walls' faces over the flat fill, rendered unlit and lit by the glow when blitted -- so the fill is lit once, as part of the image, and never drawn under it. Null until the first Update that finds a GraphicsDevice, and permanently null for a headless renderer that never gets one, which may Update but never Draw. Built on first use because UiRenderService only receives a GraphicsDevice in the host's LoadContent.</summary>
    private MapTileLayerCache? _groundCache;

    /// <summary>The cached walls' tops, a separate image from _groundCache because tops are never lit: a top is the wall's roof, rows above the floor the glow lies on.</summary>
    private MapTileLayerCache? _topsCache;

    /// <summary>The auras' glow as the texture the sprite effect reads, rebuilt when the grid or the auras change.</summary>
    private readonly MapGlowTexture _glowTexture;

    /// <summary>The grid _wallPlan was last planned for; the cached tops and the row walk's overhangs read the plan, so it must match what is drawn.</summary>
    private readonly MapBuildStamp _plannedWalls = new();

    public MapRenderer(
        IMapViewQuery mapView,
        AuraPowerView auraPower,
        MapCamera camera,
        UiRenderService renderService,
        FontService fontService,
        SpriteSheetService spriteSheetService)
    {
        _mapView = mapView;
        _auraPower = auraPower;
        _camera = camera;
        _renderService = renderService;
        _spriteSheetService = spriteSheetService;
        _glowTexture = new MapGlowTexture(new MapGlowTexels(mapView, new AuraGlowBlend(auraPower)));
        _terrainCells = new MapTerrainCells(mapView, spriteSheetService.Sprites);

        _mediumFontMeasurer = fontService.GetTextMeasurer(fontService.GetFont(GameFontChrome.MapMediumFontSize));
        _largeFontMeasurer = fontService.GetTextMeasurer(fontService.GetFont(GameFontChrome.MapLargeFontSize));
        _hugeFontMeasurer = fontService.GetTextMeasurer(fontService.GetFont(GameFontChrome.MapHugeFontSize));
        _tinyFontMeasurer = fontService.GetTextMeasurer(fontService.GetFont(GameFontChrome.MapTinyFontSize)); // ~1/3 of the medium font, for the tiny-entity grid.
        _badgeFontMeasurer = fontService.GetTextMeasurer(fontService.GetFont(GameFontChrome.MapBadgeFontSize)); // Double the tiny font, for the up/down layer-occupancy badges -- legible at a glance without competing with the main glyph.

        _lootBagSprite = spriteSheetService.Sprites.TryGetFirst(LootBagSpriteName, out var lootBagSprite) ? lootBagSprite : null;

        _drawGroundIntoCache = DrawGroundIntoCache;
        _drawTopsIntoCache = DrawTopsIntoCache;
    }

    /// <summary>Releases the cached images' render targets and the glow texture -- a host that replaces its renderer (a preview showing another build) disposes the old one.</summary>
    public void Dispose()
    {
        _groundCache?.Dispose();
        _topsCache?.Dispose();
        _glowTexture.Dispose();
        _groundCache = null;
        _topsCache = null;
    }

    /// <summary>Drops the cached images and the glow: the terrain or structures they were drawn from changed.</summary>
    /// <remarks>The glow too, so the images and the glow never disagree about which cells they describe.</remarks>
    public void InvalidateTerrain()
    {
        _groundCache?.Invalidate();
        _topsCache?.Invalidate();
        _glowTexture.Invalidate();
        _plannedWalls.Invalidate();
    }

    /// <summary>InvalidateTerrain, only when area overlaps what the images are drawn from: a neighborhood streams in a row at a time, mostly out of view.</summary>
    public void InvalidateTerrainIn(MapBounds area)
    {
        if (TouchesDrawnArea(area, _camera.CurrentScrollPosition, _camera.TileColumns, _camera.TileRows))
        {
            InvalidateTerrain();
        }
    }

    /// <summary>Whether area overlaps the tiles a terrain image of the grid at scroll reads.</summary>
    /// <remarks>The grid widened by the walls' overscan (see MapWallPlan) plus one tile, since a wall just outside the grid can reach into it and a tile next to a wall decides whether it shows its face.</remarks>
    public static bool TouchesDrawnArea(MapBounds area, Point scroll, int tileColumns, int tileRows)
    {
        var beside = SpriteReach.MaximumColumnsBeside + 1;
        var below = SpriteReach.MaximumRowsAbove + 1;
        return area.MinX < scroll.X + tileColumns + beside && area.MaxX > scroll.X - beside && area.MinY < scroll.Y + tileRows + below && area.MaxY > scroll.Y;
    }

    /// <summary>Re-renders whichever cached image is stale for mapLayer as the camera now shows it. Must run outside the UI's pass.</summary>
    /// <remarks>
    /// Rendering into MapTileLayerCache's own texture means swapping render targets, and by the time anything draws,
    /// GameLoop has already begun the UI's pass (UiRenderService.BeginFrame). Run it
    /// after anything that moves the camera this frame, so a frame that re-centres rebuilds against the new scroll
    /// position rather than drawing the uncached path once before catching up.
    /// </remarks>
    public void Update(int mapLayer)
    {
        if (_groundCache is null && _renderService.GraphicsDevice is { } graphicsDevice)
        {
            _groundCache = new MapTileLayerCache(graphicsDevice, MapFillColor);
            _topsCache = new MapTileLayerCache(graphicsDevice, Color.Transparent);
        }

        var key = MapImageKey.Of(_camera, mapLayer);
        _groundCache?.EnsureRendered(_renderService.Batch, key, _drawGroundIntoCache);
        _topsCache?.EnsureRendered(_renderService.Batch, key, _drawTopsIntoCache);
    }

    /// <summary>Draws one frame of the map: the flat fill, the ground, the walls' tops, then the row walk -- lit by the auras' glow.</summary>
    /// <remarks>
    /// Drawn into the UI's one pass (UiRenderService.Batch), in its place among the windows and under their viewport, scissor
    /// and transform: the map sets the batch's Glow for its own draws and clears it after, which ends the run, so nothing
    /// drawn before or after reads the map's light. The ground and the tops are
    /// blitted from their cached images when those match what the camera shows, and drawn directly otherwise (the frame or two
    /// before the first render, a camera moved since Update) -- the same pixels at the old cost.
    /// The glow is light, not an overlay: the effect reads it from a texture of one texel per tile (MapGlowTexels) for what
    /// stands on the ground -- the fill, the ground and walls' faces at each pixel, everything else standing along its footprint's ground row --
    /// and never for walls' tops or the cues drawn over the map (health bars, badges).
    /// </remarks>
    /// <param name="watchedEntityIds">The entities whose drawn sprites overhangs fade over, so whoever stands behind a wall stays visible.</param>
    /// <remarks>Needs the GraphicsDevice: unlike Update, it is never called on a headless renderer.</remarks>
    public void Draw(int mapLayer, IReadOnlyList<int> watchedEntityIds)
    {
        var key = MapImageKey.Of(_camera, mapLayer);
        var glowTexture = _glowTexture.Ensure(_renderService.GraphicsDevice, key, _auraPower.Version);
        var batch = _renderService.Batch;
        batch.Glow = SpriteEffectGlow.OverTileGrid(glowTexture, key.TileSize, _camera.TileOrigin(0, 0), MapGlowTexels.MarginTiles);
        try
        {
            DrawMap(batch, key, watchedEntityIds);
        }
        finally
        {
            batch.Glow = SpriteEffectGlow.None;
        }
    }

    private void DrawMap(SpriteEffectBatch batch, MapImageKey key, IReadOnlyList<int> watchedEntityIds)
    {
        var unitRectangle = _renderService.UnitRectangle;
        var litAtPixel = SpriteDrawStyle.Plain.WithGlowAtPixel();

        if (_groundCache?.Draw(batch, key, -_camera.RenderPixelOffset, litAtPixel) != true)
        {
            batch.Draw(unitRectangle, new Rectangle(0, 0, key.TileColumns * key.TileSize.X, key.TileRows * key.TileSize.Y), null, MapFillColor, litAtPixel);
            DrawGroundDirectly(batch, key, _camera.RenderPixelOffset, SpriteGlow.AtPixel);
        }
        else if (!_plannedWalls.IsCurrentFor(key))
        {
            PlanWalls(key);
        }

        if (_topsCache?.Draw(batch, key, -_camera.RenderPixelOffset, SpriteDrawStyle.Plain) != true)
        {
            DrawCachedTops(batch, key, _camera.RenderPixelOffset);
        }

        DrawOccupants(batch, unitRectangle, key, watchedEntityIds);
    }

    /// <summary>
    /// Renders the ground at whole-tile positions with no sub-tile offset, unlit, for MapTileLayerCache to capture into its
    /// texture -- the caller applies the offset once when blitting that texture, and the glow with it.
    /// </summary>
    /// <remarks>
    /// Terrain is drawn as its own full pass, ahead of every occupant, and that ordering is
    /// load-bearing rather than incidental: a multi-tile entity's sprite is drawn once from its
    /// origin tile covering its whole footprint, so a neighbouring tile's terrain draw -- a later
    /// call, and SpriteEffectBatch submits in call order with no depth buffer -- would
    /// otherwise land on top of part of that footprint. Since terrain lives in its own texture
    /// blitted before any occupant, that separation is structural rather than something the loop
    /// order has to keep getting right.
    /// </remarks>
    private void DrawGroundIntoCache(SpriteEffectBatch batch, MapImageKey key)
    {
        using (EngineHooks.FrameCost(FrameCostCategory.Update, "Map", "Terrain cache rebuild"))
        {
            DrawGroundDirectly(batch, key, Vector2.Zero, SpriteGlow.None);
        }
    }

    /// <summary>Renders the cached walls' tops at whole-tile positions for their own image, after the ground image planned them.</summary>
    private void DrawTopsIntoCache(SpriteEffectBatch batch, MapImageKey key) =>
        DrawCachedTops(batch, key, Vector2.Zero);

    /// <summary>Reads the grid's cells and plans its walls -- which pieces the images cache and which the row walk draws as overhangs.</summary>
    private void PlanWalls(MapImageKey key)
    {
        _terrainCells.Read(key.Scroll, key.TileColumns, key.TileRows, key.MapLayer);
        _wallPlan.Plan(_terrainCells, key.Scroll, key.TileColumns, key.TileRows);
        _plannedWalls.Record(key);
    }

    /// <summary>The uncached path for the ground: the same floors, off-map black and cached walls' faces MapTileLayerCache captures, drawn straight to the screen at the given sub-tile offset. Used to render into the cache (offset zero, unlit) and as Draw's fallback when the cache doesn't match (lit at each pixel, as the blit is). Either way it re-plans the walls, so the tops and the overhangs the row walk draws always match the image.</summary>
    private void DrawGroundDirectly(SpriteEffectBatch batch, MapImageKey key, Vector2 pixelOffset, SpriteGlow glow)
    {
        var tileSize = key.TileSize;
        PlanWalls(key);

        for (var columnIndex = 0; columnIndex < key.TileColumns; columnIndex++)
        {
            for (var rowIndex = 0; rowIndex < key.TileRows; rowIndex++)
            {
                var mapNodeX = columnIndex + key.Scroll.X;
                var mapNodeY = rowIndex + key.Scroll.Y;
                var tileOrigin = new Vector2(columnIndex * tileSize.X, rowIndex * tileSize.Y) - pixelOffset;

                if (!_terrainCells.IsOnMap(mapNodeX, mapNodeY))
                {
                    batch.Draw(_renderService.UnitRectangle, new Rectangle((int)tileOrigin.X, (int)tileOrigin.Y, tileSize.X, tileSize.Y), null, OffMapColor, SpriteDrawStyle.Plain);
                    continue;
                }

                DrawFloor(batch, mapNodeX, mapNodeY, tileOrigin, tileSize, glow);
            }
        }

        foreach (var piece in _wallPlan.CachedPieces)
        {
            if (!piece.IsTop)
            {
                DrawStructurePiece(batch, piece, key, pixelOffset, glow);
            }
        }
    }

    /// <summary>Every wall's top that the last wall plan caches, unlit; faces are the ground's, and the rest are the row walk's overhangs.</summary>
    private void DrawCachedTops(SpriteEffectBatch batch, MapImageKey key, Vector2 pixelOffset)
    {
        foreach (var piece in _wallPlan.CachedPieces)
        {
            if (piece.IsTop)
            {
                DrawStructurePiece(batch, piece, key, pixelOffset, SpriteGlow.None);
            }
        }
    }

    /// <summary>A wall's face or top on the tile it sits on, sized and placed by its sprite's placement.</summary>
    private void DrawStructurePiece(SpriteEffectBatch batch, MapOverhang piece, MapImageKey key, Vector2 pixelOffset, SpriteGlow glow)
    {
        var tileSize = new Vector2(key.TileSize.X, key.TileSize.Y);
        var tileOrigin = new Vector2((piece.AnchorX - key.Scroll.X) * tileSize.X, (piece.AnchorY - key.Scroll.Y) * tileSize.Y) - pixelOffset;
        DrawVisual(batch, piece.Visual, _mediumFontMeasurer, tileOrigin, tileSize, new Vector2Byte(1, 1), glow, groundRowY: 0f, out _);
    }

    /// <summary>The tile's floor, stretched to its tile -- static, so it belongs in the cached ground image. Walls are the wall plan's.</summary>
    private void DrawFloor(SpriteEffectBatch batch, int mapNodeX, int mapNodeY, Vector2 tileOrigin, Point tileSize, SpriteGlow glow)
    {
        if (_terrainCells.TryGetFloor(mapNodeX, mapNodeY, out var floor))
        {
            DrawVisual(batch, floor, _mediumFontMeasurer, tileOrigin, new Vector2(tileSize.X, tileSize.Y), footprintTiles: null, glow, groundRowY: 0f, out _);
        }
    }

    /// <summary>The y, in draw space, of the centre of a footprint's lowest row -- what something standing on it is lit along.</summary>
    private static float GroundRowY(Vector2 footprintTopLeft, Vector2 footprintSize, Point tileSize) =>
        footprintTopLeft.Y + footprintSize.Y - tileSize.Y / 2f;

    /// <summary>Every occupant of the visible grid, drawn on top of the terrain -- corpses, the Tiny sub-grid, Blocking occupants, Phasing overlays, then every tile's up/down layer badges -- in the order MapDrawOrder plans.</summary>
    /// <remarks>
    /// Rows top to bottom, each row left to right, anything that stands up drawn from the lowest row of its footprint (see
    /// MapDrawOrder): a later row overlaps an earlier one, which is what lets a sprite reach above its own tile. The order is
    /// chosen for what overlaps what, not for speed -- Map's per-cell arrays are X-fastest, and walking them row-major or
    /// column-major was measured to make no difference against the same warm cache. SpriteEffectBatch draws in call
    /// order with no depth buffer, so the plan's order is the draw order.
    /// </remarks>
    private void DrawOccupants(SpriteEffectBatch batch, Texture2D unitRectangle, MapImageKey key, IReadOnlyList<int> watchedEntityIds)
    {
        var scroll = key.Scroll;
        _drawOrder.Plan(_mapView, scroll, key.TileColumns, key.TileRows, key.MapLayer, _mapView.Bounds.Depth, _wallPlan.Overhangs);
        CollectWatchedRectangles(watchedEntityIds, key);

        var tinyTile = new Point(int.MinValue, int.MinValue);
        var tinyDrawnCount = 0;

        foreach (var item in _drawOrder.Items)
        {
            var footprintTopLeft = _camera.TileOrigin(item.MapX - scroll.X, item.MapY - scroll.Y);
            var footprintSize = new Vector2(item.Size.X * key.TileSize.X, item.Size.Y * key.TileSize.Y);

            switch (item.Kind)
            {
                case MapDrawItemKind.Corpse:
                    TryDrawEntityVisual(batch, item.EntityId, FontMeasurerForSize(item.Size.X), footprintTopLeft, footprintSize, item.Size, GroundRowY(footprintTopLeft, footprintSize, key.TileSize));
                    DrawLootBagBadge(batch, _mapView.GetStatus(item.EntityId).LootBag, footprintTopLeft, footprintSize, key.TileSize);
                    break;

                case MapDrawItemKind.Tiny:
                    if (tinyTile.X != item.MapX || tinyTile.Y != item.MapY)
                    {
                        tinyTile = new Point(item.MapX, item.MapY);
                        tinyDrawnCount = 0;
                    }

                    if (tinyDrawnCount < MaxTinyEntitiesDrawn && DrawTinyOccupant(batch, item.EntityId, footprintTopLeft, key.TileSize, tinyDrawnCount))
                    {
                        tinyDrawnCount++;
                    }

                    break;

                case MapDrawItemKind.Blocking:
                    DrawPrimaryOccupant(batch, unitRectangle, item.EntityId, item.Size, footprintTopLeft, footprintSize, key.TileSize);
                    break;

                case MapDrawItemKind.Phasing:
                    TryDrawEntityVisual(batch, item.EntityId, FontMeasurerForSize(item.Size.X), footprintTopLeft, footprintSize, item.Size, GroundRowY(footprintTopLeft, footprintSize, key.TileSize), PhasingAlphaMultiplier);
                    break;

                case MapDrawItemKind.LayerBadge:
                    DrawLayerBadges(batch, item.HasHigherLayer, item.HasLowerLayer, footprintTopLeft, key.TileSize);
                    break;

                case MapDrawItemKind.Overhang:
                    DrawOverhang(batch, _wallPlan.Overhangs[item.EntityId], footprintTopLeft, footprintSize);
                    break;
            }
        }
    }

    /// <summary>Where each watched entity's sprite is drawn this frame, for OverhangFade.</summary>
    private void CollectWatchedRectangles(IReadOnlyList<int> watchedEntityIds, MapImageKey key)
    {
        _watchedRectangles.Clear();

        for (var watchedIndex = 0; watchedIndex < watchedEntityIds.Count; watchedIndex++)
        {
            var entityId = watchedEntityIds[watchedIndex];
            if (!_mapView.TryGetOccupant(entityId, out var occupant) || !_mapView.TryGetVisual(entityId, out var visual))
            {
                continue;
            }

            var footprintTopLeft = _camera.TileOrigin(occupant.Position.X - key.Scroll.X, occupant.Position.Y - key.Scroll.Y);
            var footprintSize = new Vector2(occupant.Size.X * key.TileSize.X, occupant.Size.Y * key.TileSize.Y);
            _watchedRectangles.Add(OverhangFade.WatchedRectangle(footprintTopLeft, footprintSize, occupant.Size, _spriteSheetService.Sprites.Resolve(visual).Sprite));
        }
    }

    /// <summary>A wall's live face or top, faded where it is drawn over a watched entity (OverhangFade). A face is lit at each pixel, as a cached face is when the ground image is blitted, so a wall reads the same whichever way its face is drawn; a top is never lit.</summary>
    private void DrawOverhang(SpriteEffectBatch batch, MapOverhang overhang, Vector2 tileOrigin, Vector2 tileSize)
    {
        var overhangRectangle = OverhangFade.WatchedRectangle(tileOrigin, tileSize, new Vector2Byte(1, 1), overhang.Visual.Sprite);
        var alpha = OverhangFade.AlphaFor(overhangRectangle, _watchedRectangles);
        var glow = overhang.IsTop ? SpriteGlow.None : SpriteGlow.AtPixel;
        DrawVisual(batch, overhang.Visual, _mediumFontMeasurer, tileOrigin, tileSize, new Vector2Byte(1, 1), glow, groundRowY: 0f, out _, alpha);
    }

    /// <summary>Draws entityId's sprite if it has one, else falls back to its glyph, lit along groundRowY -- see DrawVisual. Returns whether anything was actually drawn.</summary>
    private bool TryDrawEntityVisual(SpriteEffectBatch batch, int entityId, FontStashTextMeasurer fontMeasurer, Vector2 footprintTopLeft, Vector2 footprintSize, Vector2Byte footprintTiles, float groundRowY, float alphaMultiplier = 1f) =>
        TryDrawEntityVisual(batch, entityId, fontMeasurer, footprintTopLeft, footprintSize, footprintTiles, groundRowY, out _, alphaMultiplier);

    /// <inheritdoc cref="TryDrawEntityVisual(SpriteEffectBatch, int, FontStashTextMeasurer, Vector2, Vector2, Vector2Byte, float, float)"/>
    /// <param name="drawnTop">Where the top of what was drawn landed, for cues that sit above it; the footprint's top for a glyph or a miss.</param>
    private bool TryDrawEntityVisual(SpriteEffectBatch batch, int entityId, FontStashTextMeasurer fontMeasurer, Vector2 footprintTopLeft, Vector2 footprintSize, Vector2Byte footprintTiles, float groundRowY, out float drawnTop, float alphaMultiplier = 1f)
    {
        drawnTop = footprintTopLeft.Y;
        return _mapView.TryGetVisual(entityId, out var visual) && DrawVisual(batch, _spriteSheetService.Sprites.Resolve(visual), fontMeasurer, footprintTopLeft, footprintSize, footprintTiles, SpriteGlow.AlongGroundRow, groundRowY, out drawnTop, alphaMultiplier);
    }

    /// <summary>The one place that turns a resolved visual into a draw, shared by every per-tile visual draw. A sprite with a placement is sized and positioned by it (SpritePlacementMath) -- it may reach above and beside its footprint -- and one without fills the footprint, as a glyph does. A corpse draws in its own colours, desaturated by the sprite effect (MapVisual.DrawStyle). Delegates the actual draw to SpriteOrGlyphRenderer, shared with Folder/inventory item cells.</summary>
    /// <param name="footprintTiles">The footprint in tiles, which a placed sprite scales with; null draws a sprite stretched to the footprint whatever its placement (a floor).</param>
    /// <param name="glow">Where it reads the glow: at each pixel for a floor or a face, along groundRowY for anything else standing, never for a top.</param>
    private bool DrawVisual(SpriteEffectBatch batch, MapVisual visual, FontStashTextMeasurer fontMeasurer, Vector2 footprintTopLeft, Vector2 footprintSize, Vector2Byte? footprintTiles, SpriteGlow glow, float groundRowY, out float drawnTop, float alphaMultiplier = 1f)
    {
        drawnTop = footprintTopLeft.Y;
        var style = visual.DrawStyle with { Glow = glow, GroundRowY = groundRowY };
        if (visual.Sprite is not { } sprite)
        {
            return SpriteOrGlyphRenderer.Draw(batch, _spriteSheetService, (SpriteView?)null, fontMeasurer, visual.Glyph, visual.GlyphColor, footprintTopLeft, footprintSize, Color.White, alphaMultiplier, outline: true, style);
        }

        var (drawnTopLeft, drawnSize) = footprintTiles is { } tiles
            ? SpritePlacementMath.DrawnRectangle(footprintTopLeft, footprintSize, tiles, new Point(sprite.SourceRectangle.Width, sprite.SourceRectangle.Height), sprite.Placement)
            : (footprintTopLeft, footprintSize);
        drawnTop = drawnTopLeft.Y;
        return SpriteOrGlyphRenderer.Draw(batch, _spriteSheetService, sprite, fontMeasurer, string.Empty, Color.White, drawnTopLeft, drawnSize, Color.White, alphaMultiplier, outline: true, style);
    }

    /// <summary>One Tiny occupant in the tile's 3x3 sub-grid, at the cell for how many of the tile's Tiny occupants were drawn before it, lit along its tile's row. Returns whether anything was drawn, so an occupant with no visual doesn't take a cell.</summary>
    private bool DrawTinyOccupant(SpriteEffectBatch batch, int entityId, Vector2 tileOrigin, Point tileSize, int drawnBefore)
    {
        var subCellSize = new Point(tileSize.X / TinyGridDimension, tileSize.Y / TinyGridDimension);
        var subColumn = drawnBefore % TinyGridDimension;
        var subRow = drawnBefore / TinyGridDimension;
        var subCellTopLeft = new Vector2(tileOrigin.X + subColumn * subCellSize.X, tileOrigin.Y + subRow * subCellSize.Y);

        return TryDrawEntityVisual(batch, entityId, _tinyFontMeasurer, subCellTopLeft, new Vector2(subCellSize.X, subCellSize.Y), new Vector2Byte(1, 1), tileOrigin.Y + tileSize.Y / 2f);
    }

    /// <summary>A Blocking occupant, its health bar and loot badge, its charging badge and its dodge glow. The health bar and charging badge follow the drawn sprite (a tall sprite's head would otherwise cover them); the loot badge and dodge glow are about the footprint and stay on it.</summary>
    private void DrawPrimaryOccupant(SpriteEffectBatch batch, Texture2D unitRectangle, int entityId, Vector2Byte size, Vector2 footprintTopLeft, Vector2 footprintSize, Point tileSize)
    {
        var status = _mapView.GetStatus(entityId);

        TryDrawEntityVisual(batch, entityId, FontMeasurerForSize(size.X), footprintTopLeft, footprintSize, size, GroundRowY(footprintTopLeft, footprintSize, tileSize), out var drawnTop);
        var cueTop = SpritePlacementMath.CueTop(drawnTop, footprintTopLeft.Y);
        DrawEntityIcons(batch, unitRectangle, entityId, status, footprintTopLeft, footprintSize, tileSize, cueTop);

        // Unlike DrawEntityIcons (health bar/loot badge), the charging badge must show for the
        // player too -- drawn here, directly, rather than inside DrawEntityIcons' own early
        // player-skip guard.
        DrawChargingBadge(batch, entityId, footprintTopLeft, footprintSize, tileSize, cueTop);

        // Dodging is a temporary, brief immunity window (Combat Overhaul: Dodge) -- an inner-fade
        // glow framing the entity's own footprint on top of its sprite (the same GlowMode.InteriorFade
        // ring technique the selected-tile glow uses), not a sprite-opacity fade: a translucent
        // sprite read as the entity vanishing outright rather than as a status cue (confirmed live).
        // GlowRenderer's own default rings top out at 50% alpha on the outermost ring, fading to 10%
        // on the innermost -- tuned for a tile-selection cue looked at at leisure, not a fast, brief
        // (as little as 0.5s at low Dexterity) combat status cue -- DodgingGlowAlphaMultiplier boosts
        // the outer rings toward fully opaque so the fade is still visible at a glance, confirmed live
        // as too subtle to notice at the un-boosted default.
        if (status.IsDodging)
        {
            GlowRenderer.Draw(batch, unitRectangle, new Rectangle((int)footprintTopLeft.X, (int)footprintTopLeft.Y, (int)footprintSize.X, (int)footprintSize.Y), DodgingGlowColor, GlowMode.InteriorFade, DodgingGlowAlphaMultiplier);
        }
    }

    /// <summary>
    /// The LootBag-Red badge, shared by a corpse (DrawOccupants) and a live, still-Blocking
    /// container (DrawEntityIcons -- lootable while alive, unlike a corpse). Nothing when the
    /// entity carries no items; full color if its loot window has never been opened, desaturated
    /// once it has, as a corpse is. Small and single-tile-sized, anchored to the top-right corner of the entity's own full
    /// footprint rather than its origin tile, so a multi-tile corpse gets its badge on its actual
    /// top-right tile.
    /// </summary>
    private void DrawLootBagBadge(SpriteEffectBatch batch, LootBagState lootBag, Vector2 footprintTopLeft, Vector2 footprintSize, Point tileSize)
    {
        if (lootBag == LootBagState.None || _lootBagSprite is not { } lootBagSprite)
        {
            return;
        }

        var style = lootBag == LootBagState.Looted ? SpriteDrawStyle.Desaturated : SpriteDrawStyle.Plain;
        var badgeSize = new Vector2(tileSize.X, tileSize.Y) * LootBagBadgeSizeFraction;
        var badgePosition = new Vector2(footprintTopLeft.X + footprintSize.X - badgeSize.X, footprintTopLeft.Y);

        SpriteOrGlyphRenderer.Draw(batch, _spriteSheetService, lootBagSprite, _badgeFontMeasurer, string.Empty, Color.White, badgePosition, badgeSize, Color.White, outline: true, style: style);
    }

    /// <summary>
    /// While entityId is mid-windup on a Delayed action, draws that action's own sprite/glyph as a small badge centered
    /// above its footprint -- "put that action/item's sprite as a badge above their sprite on the map" (Combat Overhaul:
    /// Dodge, TODO.md). Positioned a full badge-height above cueTop -- the top of the drawn sprite, or the footprint's if
    /// that is higher -- so it never collides with DrawHealthBar's own bar, which sits inside the footprint's top edge.
    /// Called directly from DrawPrimaryOccupant, not from DrawEntityIcons, since the player must see their own charging
    /// badge too and DrawEntityIcons deliberately skips the player entirely.
    /// </summary>
    private void DrawChargingBadge(SpriteEffectBatch batch, int entityId, Vector2 footprintTopLeft, Vector2 footprintSize, Point tileSize, float cueTop)
    {
        if (!_mapView.TryGetChargingAction(entityId, out var action))
        {
            return;
        }

        var badgeSize = new Vector2(tileSize.X, tileSize.Y) * ChargingBadgeSizeFraction;
        var badgePosition = new Vector2(footprintTopLeft.X + (footprintSize.X - badgeSize.X) / 2f, cueTop - badgeSize.Y);

        SpriteView? actionSprite = action.SpriteName is { } spriteName && _spriteSheetService.Sprites.TryGetFirst(spriteName, out var icon) ? icon : null;
        SpriteOrGlyphRenderer.Draw(batch, _spriteSheetService, actionSprite, _badgeFontMeasurer, action.Glyph, action.GlyphColor, badgePosition, badgeSize, Color.White, outline: true);
    }

    private void DrawEntityIcons(SpriteEffectBatch batch, Texture2D unitRectangle, int entityId, EntityStatusView status, Vector2 footprintTopLeft, Vector2 footprintSize, Point tileSize, float cueTop)
    {
        if (entityId == _mapView.PlayerEntityId)
        {
            return;
        }

        if (status.HealthFraction is { } healthFraction)
        {
            DrawHealthBar(batch, unitRectangle, healthFraction, footprintTopLeft, footprintSize, cueTop);
        }

        // A container is lootable while alive, unlike a creature (only lootable once dead, see
        // DrawOccupants' corpse call to the same badge helper) -- gated on IsContainer, not
        // just "carries items," since a live goblin/fairy/player also carries its own inventory
        // stacks and must not show a loot-bag badge for those.
        if (status.IsContainer)
        {
            DrawLootBagBadge(batch, status.LootBag, footprintTopLeft, footprintSize, tileSize);
        }
    }

    /// <summary>Thin bar at cueTop -- the top of the drawn sprite, or of the footprint if that is higher -- only drawn below full health (EntityStatusView.HealthFraction is null otherwise). Black backdrop doubles as the outline and the "missing health" portion; the fill rect insets 1px and its width (not the outline's) scales with the health fraction.</summary>
    private static void DrawHealthBar(SpriteEffectBatch batch, Texture2D unitRectangle, float healthFraction, Vector2 footprintTopLeft, Vector2 footprintSize, float cueTop)
    {
        var barWidth = footprintSize.X * HealthBarWidthFraction;
        var barX = footprintTopLeft.X + (footprintSize.X - barWidth) / 2f;
        var barY = cueTop;

        var outerRectangle = new Rectangle((int)barX, (int)barY, (int)barWidth, HealthBarHeightPixels);
        batch.Draw(unitRectangle, outerRectangle, null, HealthBarPalette.OutlineColor, SpriteDrawStyle.Plain);

        var innerWidth = (int)((outerRectangle.Width - 2) * healthFraction);
        if (innerWidth > 0)
        {
            batch.Draw(unitRectangle, new Rectangle(outerRectangle.X + 1, outerRectangle.Y + 1, innerWidth, HealthBarHeightPixels - 2), null, HealthBarPalette.FractionColor(healthFraction), SpriteDrawStyle.Plain);
        }
    }

    /// <summary>Medium/large/huge glyph font, measured, by an entity's footprint width.</summary>
    private FontStashTextMeasurer FontMeasurerForSize(int sizeX) => sizeX switch
    {
        1 => _mediumFontMeasurer,
        2 => _largeFontMeasurer,
        _ => _hugeFontMeasurer,
    };

    /// <summary>
    /// Blue up-arrow (top-right) if any layer above the current one is occupied; brown
    /// down-arrow (bottom-right) if any layer below is. A tile-level badge -- unlike
    /// DrawEntityIcons, this describes the tile's other layers, not the Blocking occupant
    /// drawn on it -- so MapDrawOrder plans every badge after every occupant, where no later
    /// row's tall sprite can paint over it.
    /// </summary>
    private void DrawLayerBadges(SpriteEffectBatch batch, bool hasHigherLayer, bool hasLowerLayer, Vector2 tileOrigin, Point tileSize)
    {
        if (hasHigherLayer)
        {
            var drawPosition = new Vector2(tileOrigin.X + tileSize.X - _badgeFontMeasurer.Font.LineHeight, tileOrigin.Y);
            LabelRenderer.Draw(batch, _badgeFontMeasurer.Font, "^", drawPosition, UpLayerBadgeColor, outline: true);
        }

        if (hasLowerLayer)
        {
            var drawPosition = new Vector2(tileOrigin.X + tileSize.X - _badgeFontMeasurer.Font.LineHeight, tileOrigin.Y + tileSize.Y - _badgeFontMeasurer.Font.LineHeight);
            LabelRenderer.Draw(batch, _badgeFontMeasurer.Font, "v", drawPosition, DownLayerBadgeColor, outline: true);
        }
    }
}
