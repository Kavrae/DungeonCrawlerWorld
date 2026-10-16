using Engine.Diagnostics;
using Engine.Math;
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

/// <summary>Draws the map as the world looks through a camera: the flat fill, terrain and walls, everything standing on them, and the aura glow.</summary>
/// <remarks>
/// <para>
/// Reads the world only through IMapViewQuery and AuraPowerView, so the game feeds it a live session and a map editor an
/// authored layout. Draws nothing about what the player is doing (targeting, selection): that is drawn over it by its host.
/// </para>
/// <para>
/// Two calls a frame: Update, outside any SpriteBatch pass, re-renders the cached terrain and glow images when they are
/// stale; Draw blits them around the row walk. Each image is stale when the camera shows something other than what it was
/// rendered for (MapImageKey), so no camera move has to announce itself. What the key can't see is told: terrain
/// changes through InvalidateTerrain/InvalidateTerrainIn, and aura changes through AuraPowerView.Version, read here.
/// </para>
/// <para>
/// Draw order is significant, not incidental -- each pass lands on top of the previous one with no depth buffer
/// (SpriteSortMode.Deferred submits in call order): the flat fill first so everything else has something to draw over,
/// then terrain (a terrain's sprite is its background; terrain with none shows the flat fill under its glyph), the row
/// walk, and the glow over all of it.
/// </para>
/// </remarks>
public sealed class MapRenderer
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

    /// <summary>The summed aura power at a tile that glows at full brightness; anything more glows no brighter.</summary>
    private const int FullGlowPower = 8;

    /// <summary>Halves the glow so a full-strength one still lets whatever's standing on that tile -- terrain, an occupant sprite/glyph -- read through, rather than washing it out at the source tile itself.</summary>
    private const float GlowOpacityMultiplier = 0.5f;

    private static readonly Color MapFillColor = new(40, 40, 40);

    /// <summary>What a tile off the loaded map is drawn as, so the edge of the world reads as an edge rather than as floor with nothing on it.</summary>
    private static readonly Color OffMapColor = Color.Black;

    private readonly IMapViewQuery _mapView;
    private readonly AuraPowerView _auraPower;
    private readonly MapCamera _camera;
    private readonly ElementPoolService _elementPoolService;
    private readonly SpriteSheetService _spriteSheetService;
    private readonly LabelRenderer _labelRenderer;
    private readonly AuraGlowBlend _auraGlowBlend;

    private readonly SpriteFontBase _mediumFont;
    private readonly SpriteFontBase _largeFont;
    private readonly SpriteFontBase _hugeFont;
    private readonly SpriteFontBase _tinyFont;
    private readonly SpriteFontBase _badgeFont;

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
    private readonly Action<SpriteBatch, MapImageKey> _drawTerrainIntoCache;
    private readonly Action<SpriteBatch, MapImageKey> _drawGlowOverlayIntoCache;

    /// <summary>Null until the first Update that finds a GraphicsDevice, and permanently null for a headless renderer that never gets one -- Draw's fallback paths cover both cases. Built on first use because ElementPoolService only receives a GraphicsDevice in GameLoop.LoadContent.</summary>
    private MapTileLayerCache? _terrainCache;

    /// <summary>The aura glow overlay's own cached rendering -- a separate texture from _terrainCache because it is blitted on the other side of the occupants.</summary>
    private MapTileLayerCache? _glowCache;

    /// <summary>The AuraPowerView.Version _glowCache was last rendered against -- a source appearing, moving or expiring changes the glow without changing the camera.</summary>
    private int _renderedGlowVersion = -1;

    public MapRenderer(
        IMapViewQuery mapView,
        AuraPowerView auraPower,
        MapCamera camera,
        ElementPoolService elementPoolService,
        FontService fontService,
        SpriteSheetService spriteSheetService,
        LabelRenderer labelRenderer)
    {
        _mapView = mapView;
        _auraPower = auraPower;
        _camera = camera;
        _elementPoolService = elementPoolService;
        _spriteSheetService = spriteSheetService;
        _labelRenderer = labelRenderer;
        _auraGlowBlend = new AuraGlowBlend(auraPower);
        _terrainCells = new MapTerrainCells(mapView, spriteSheetService.Sprites);

        _mediumFont = fontService.GetFont(GameFontChrome.MapMediumFontSize);
        _largeFont = fontService.GetFont(GameFontChrome.MapLargeFontSize);
        _hugeFont = fontService.GetFont(GameFontChrome.MapHugeFontSize);
        _tinyFont = fontService.GetFont(GameFontChrome.MapTinyFontSize); // ~1/3 of _mediumFont, for the tiny-entity grid.
        _badgeFont = fontService.GetFont(GameFontChrome.MapBadgeFontSize); // Double _tinyFont, for the up/down layer-occupancy badges -- legible at a glance without competing with the main glyph.

        _lootBagSprite = spriteSheetService.Sprites.TryGetFirst(LootBagSpriteName, out var lootBagSprite) ? lootBagSprite : null;

        _drawTerrainIntoCache = DrawTerrainIntoCache;
        _drawGlowOverlayIntoCache = DrawGlowOverlayIntoCache;
    }

    /// <summary>Drops both cached images: the terrain or structures they were drawn from changed.</summary>
    /// <remarks>Both, not only the terrain image, so the terrain image and the glow never disagree about which cells they describe.</remarks>
    public void InvalidateTerrain()
    {
        _terrainCache?.Invalidate();
        _glowCache?.Invalidate();
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

    /// <summary>Re-renders whichever cached image is stale for mapLayer as the camera now shows it. Must run outside any SpriteBatch pass.</summary>
    /// <remarks>
    /// Rendering into MapTileLayerCache's own texture means swapping render targets, and by the time anything draws,
    /// GameLoop has already begun the shared SpriteBatch pass that ElementPoolService's render-state stack owns. Run it
    /// after anything that moves the camera this frame, so a frame that re-centres rebuilds against the new scroll
    /// position rather than drawing the uncached path once before catching up.
    /// </remarks>
    public void Update(int mapLayer)
    {
        if (_terrainCache is null && _elementPoolService.GraphicsDevice is { } graphicsDevice)
        {
            _terrainCache = new MapTileLayerCache(graphicsDevice);
            _glowCache = new MapTileLayerCache(graphicsDevice);
        }

        if (_renderedGlowVersion != _auraPower.Version)
        {
            _renderedGlowVersion = _auraPower.Version;
            _glowCache?.Invalidate();
        }

        var key = MapImageKey.Of(_camera, mapLayer);
        _terrainCache?.EnsureRendered(_elementPoolService.SpriteBatch, key, _drawTerrainIntoCache);
        _glowCache?.EnsureRendered(_elementPoolService.SpriteBatch, key, _drawGlowOverlayIntoCache);
    }

    /// <summary>Draws one frame of the map: the flat fill, terrain, the row walk, then the glow.</summary>
    /// <remarks>Terrain and glow are blitted from their cached images when those match what the camera shows, and drawn directly otherwise (the frame or two before the first render, a camera moved since Update, a headless renderer) -- the same pixels at the old cost.</remarks>
    /// <param name="watchedEntityIds">The entities whose drawn sprites overhangs fade over, so whoever stands behind a wall stays visible.</param>
    public void Draw(SpriteBatch spriteBatch, int mapLayer, IReadOnlyList<int> watchedEntityIds)
    {
        var unitRectangle = _elementPoolService.UnitRectangle;
        var key = MapImageKey.Of(_camera, mapLayer);

        spriteBatch.Draw(unitRectangle, new Rectangle(0, 0, key.TileColumns * key.TileSize.X, key.TileRows * key.TileSize.Y), MapFillColor);

        if (_terrainCache?.Draw(spriteBatch, key, -_camera.RenderPixelOffset) != true)
        {
            DrawTerrainDirectly(spriteBatch, key, _camera.RenderPixelOffset);
        }

        DrawOccupants(spriteBatch, unitRectangle, key, watchedEntityIds);

        if (_glowCache?.Draw(spriteBatch, key, -_camera.RenderPixelOffset) != true)
        {
            DrawGlowOverlay(spriteBatch, unitRectangle, key, _camera.RenderPixelOffset);
        }
    }

    /// <summary>Renders the glow overlay at whole-tile positions with no sub-tile offset, for MapTileLayerCache to capture -- the caller applies the offset once when blitting.</summary>
    private void DrawGlowOverlayIntoCache(SpriteBatch spriteBatch, MapImageKey key) =>
        DrawGlowOverlay(spriteBatch, _elementPoolService.UnitRectangle, key, Vector2.Zero);

    /// <summary>
    /// The auras' glow (see AuraGlowBlend), drawn as a translucent overlay on top of terrain and
    /// occupant sprites, so it shows the same over a full-tile sprite as over a glyph on the flat fill.
    /// </summary>
    private void DrawGlowOverlay(SpriteBatch spriteBatch, Texture2D unitRectangle, MapImageKey key, Vector2 pixelOffset)
    {
        _auraGlowBlend.BeginPass();

        for (var columnIndex = 0; columnIndex < key.TileColumns; columnIndex++)
        {
            for (var rowIndex = 0; rowIndex < key.TileRows; rowIndex++)
            {
                var mapNodeX = columnIndex + key.Scroll.X;
                var mapNodeY = rowIndex + key.Scroll.Y;

                if (!_mapView.IsOnMap(new Vector3Int(mapNodeX, mapNodeY, 0)) || !_auraGlowBlend.TryGetGlow(mapNodeX, mapNodeY, key.MapLayer, out var glowColor, out var totalPower))
                {
                    continue;
                }

                var tileOrigin = new Vector2(columnIndex * key.TileSize.X, rowIndex * key.TileSize.Y) - pixelOffset;
                var destination = new Rectangle((int)tileOrigin.X, (int)tileOrigin.Y, key.TileSize.X, key.TileSize.Y);
                var glowFactor = Math.Min(totalPower, FullGlowPower) / (float)FullGlowPower;
                spriteBatch.Draw(unitRectangle, destination, glowColor * glowFactor * GlowOpacityMultiplier);
            }
        }
    }

    /// <summary>
    /// Renders the terrain at whole-tile positions with no sub-tile offset,
    /// for MapTileLayerCache to capture into its texture -- the caller applies the offset once when
    /// blitting that texture instead of every tile applying it individually.
    /// </summary>
    /// <remarks>
    /// Terrain is drawn as its own full pass, ahead of every occupant, and that ordering is
    /// load-bearing rather than incidental: a multi-tile entity's sprite is drawn once from its
    /// origin tile covering its whole footprint, so a neighbouring tile's terrain draw -- a later
    /// call, and SpriteSortMode.Deferred submits in call order with no depth buffer -- would
    /// otherwise land on top of part of that footprint. Since terrain lives in its own texture
    /// blitted before any occupant, that separation is structural rather than something the loop
    /// order has to keep getting right.
    /// </remarks>
    private void DrawTerrainIntoCache(SpriteBatch spriteBatch, MapImageKey key)
    {
        using (EngineHooks.FrameCost(FrameCostCategory.Update, "Map", "Terrain cache rebuild"))
        {
            DrawTerrainDirectly(spriteBatch, key, Vector2.Zero);
        }
    }

    /// <summary>The uncached path: the same floors, walls and off-map black MapTileLayerCache captures, drawn straight to the screen at the given sub-tile offset. Used to render into the cache (offset zero) and as Draw's fallback when the cache doesn't match. Either way it rebuilds the overhang list the row walk draws, so the list always matches the image.</summary>
    private void DrawTerrainDirectly(SpriteBatch spriteBatch, MapImageKey key, Vector2 pixelOffset)
    {
        var tileSize = key.TileSize;
        _terrainCells.Read(key.Scroll, key.TileColumns, key.TileRows, key.MapLayer);

        for (var columnIndex = 0; columnIndex < key.TileColumns; columnIndex++)
        {
            for (var rowIndex = 0; rowIndex < key.TileRows; rowIndex++)
            {
                var mapNodeX = columnIndex + key.Scroll.X;
                var mapNodeY = rowIndex + key.Scroll.Y;
                var tileOrigin = new Vector2(columnIndex * tileSize.X, rowIndex * tileSize.Y) - pixelOffset;

                if (!_terrainCells.IsOnMap(mapNodeX, mapNodeY))
                {
                    spriteBatch.Draw(_elementPoolService.UnitRectangle, new Rectangle((int)tileOrigin.X, (int)tileOrigin.Y, tileSize.X, tileSize.Y), OffMapColor);
                    continue;
                }

                DrawFloor(spriteBatch, mapNodeX, mapNodeY, tileOrigin, tileSize);
            }
        }

        DrawStructures(spriteBatch, key, pixelOffset);
    }

    /// <summary>Every wall's face and top that MapWallPlan caches, faces then tops; the rest it leaves on its Overhangs for the row walk.</summary>
    private void DrawStructures(SpriteBatch spriteBatch, MapImageKey key, Vector2 pixelOffset)
    {
        _wallPlan.Plan(_terrainCells, key.Scroll, key.TileColumns, key.TileRows);

        foreach (var piece in _wallPlan.CachedPieces)
        {
            DrawStructurePiece(spriteBatch, piece, key, pixelOffset);
        }
    }

    /// <summary>A wall's face or top on the tile it sits on, sized and placed by its sprite's placement.</summary>
    private void DrawStructurePiece(SpriteBatch spriteBatch, MapOverhang piece, MapImageKey key, Vector2 pixelOffset)
    {
        var tileSize = new Vector2(key.TileSize.X, key.TileSize.Y);
        var tileOrigin = new Vector2((piece.AnchorX - key.Scroll.X) * tileSize.X, (piece.AnchorY - key.Scroll.Y) * tileSize.Y) - pixelOffset;
        DrawVisual(spriteBatch, piece.Visual, _mediumFont, tileOrigin, tileSize, new Vector2Byte(1, 1), out _);
    }

    /// <summary>The tile's floor, stretched to its tile -- static, so it belongs in the cached terrain texture. Walls are DrawStructures'.</summary>
    private void DrawFloor(SpriteBatch spriteBatch, int mapNodeX, int mapNodeY, Vector2 tileOrigin, Point tileSize)
    {
        if (_terrainCells.TryGetFloor(mapNodeX, mapNodeY, out var floor))
        {
            DrawVisual(spriteBatch, floor, _mediumFont, tileOrigin, new Vector2(tileSize.X, tileSize.Y), footprintTiles: null, out _);
        }
    }

    /// <summary>Every occupant of the visible grid, drawn on top of the terrain -- corpses, the Tiny sub-grid, Blocking occupants, Phasing overlays, then every tile's up/down layer badges -- in the order MapDrawOrder plans.</summary>
    /// <remarks>
    /// Rows top to bottom, each row left to right, anything that stands up drawn from the lowest row of its footprint (see
    /// MapDrawOrder): a later row overlaps an earlier one, which is what lets a sprite reach above its own tile. The order is
    /// chosen for what overlaps what, not for speed -- Map's per-cell arrays are X-fastest, and walking them row-major or
    /// column-major was measured to make no difference against the same warm cache. SpriteSortMode.Deferred draws in call
    /// order with no depth buffer, so the plan's order is the draw order.
    /// </remarks>
    private void DrawOccupants(SpriteBatch spriteBatch, Texture2D unitRectangle, MapImageKey key, IReadOnlyList<int> watchedEntityIds)
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
                    TryDrawEntityVisual(spriteBatch, item.EntityId, FontForSize(item.Size.X), footprintTopLeft, footprintSize, item.Size);
                    DrawLootBagBadge(spriteBatch, _mapView.GetStatus(item.EntityId).LootBag, footprintTopLeft, footprintSize, key.TileSize);
                    break;

                case MapDrawItemKind.Tiny:
                    if (tinyTile.X != item.MapX || tinyTile.Y != item.MapY)
                    {
                        tinyTile = new Point(item.MapX, item.MapY);
                        tinyDrawnCount = 0;
                    }

                    if (tinyDrawnCount < MaxTinyEntitiesDrawn && DrawTinyOccupant(spriteBatch, item.EntityId, footprintTopLeft, key.TileSize, tinyDrawnCount))
                    {
                        tinyDrawnCount++;
                    }

                    break;

                case MapDrawItemKind.Blocking:
                    DrawPrimaryOccupant(spriteBatch, unitRectangle, item.EntityId, item.Size, footprintTopLeft, footprintSize, key.TileSize);
                    break;

                case MapDrawItemKind.Phasing:
                    TryDrawEntityVisual(spriteBatch, item.EntityId, FontForSize(item.Size.X), footprintTopLeft, footprintSize, item.Size, PhasingAlphaMultiplier);
                    break;

                case MapDrawItemKind.LayerBadge:
                    DrawLayerBadges(spriteBatch, item.HasHigherLayer, item.HasLowerLayer, footprintTopLeft, key.TileSize);
                    break;

                case MapDrawItemKind.Overhang:
                    DrawOverhang(spriteBatch, _wallPlan.Overhangs[item.EntityId], footprintTopLeft, footprintSize);
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

    /// <summary>A wall's live face or top, faded where it is drawn over a watched entity (OverhangFade).</summary>
    private void DrawOverhang(SpriteBatch spriteBatch, MapOverhang overhang, Vector2 tileOrigin, Vector2 tileSize)
    {
        var overhangRectangle = OverhangFade.WatchedRectangle(tileOrigin, tileSize, new Vector2Byte(1, 1), overhang.Visual.Sprite);
        var alpha = OverhangFade.AlphaFor(overhangRectangle, _watchedRectangles);
        DrawVisual(spriteBatch, overhang.Visual, _mediumFont, tileOrigin, tileSize, new Vector2Byte(1, 1), out _, alpha);
    }

    /// <summary>Draws entityId's sprite if it has one, else falls back to its glyph -- see DrawVisual. Returns whether anything was actually drawn.</summary>
    private bool TryDrawEntityVisual(SpriteBatch spriteBatch, int entityId, SpriteFontBase font, Vector2 footprintTopLeft, Vector2 footprintSize, Vector2Byte footprintTiles, float alphaMultiplier = 1f) =>
        TryDrawEntityVisual(spriteBatch, entityId, font, footprintTopLeft, footprintSize, footprintTiles, out _, alphaMultiplier);

    /// <inheritdoc cref="TryDrawEntityVisual(SpriteBatch, int, SpriteFontBase, Vector2, Vector2, Vector2Byte, float)"/>
    /// <param name="drawnTop">Where the top of what was drawn landed, for cues that sit above it; the footprint's top for a glyph or a miss.</param>
    private bool TryDrawEntityVisual(SpriteBatch spriteBatch, int entityId, SpriteFontBase font, Vector2 footprintTopLeft, Vector2 footprintSize, Vector2Byte footprintTiles, out float drawnTop, float alphaMultiplier = 1f)
    {
        drawnTop = footprintTopLeft.Y;
        return _mapView.TryGetVisual(entityId, out var visual) && DrawVisual(spriteBatch, _spriteSheetService.Sprites.Resolve(visual), font, footprintTopLeft, footprintSize, footprintTiles, out drawnTop, alphaMultiplier);
    }

    /// <summary>The one place that turns a resolved visual into a draw, shared by every per-tile visual draw. A sprite with a placement is sized and positioned by it (SpritePlacementMath) -- it may reach above and beside its footprint -- and one without fills the footprint, as a glyph does. A corpse draws with a flat Color.Gray tint instead of its normal color -- a color-multiply override, not a true desaturation shader (no shader/Effect infrastructure exists here). Delegates the actual draw to SpriteOrGlyphRenderer, shared with Folder/inventory item cells.</summary>
    /// <param name="footprintTiles">The footprint in tiles, which a placed sprite scales with; null draws a sprite stretched to the footprint whatever its placement (a floor).</param>
    private bool DrawVisual(SpriteBatch spriteBatch, MapVisual visual, SpriteFontBase font, Vector2 footprintTopLeft, Vector2 footprintSize, Vector2Byte? footprintTiles, out float drawnTop, float alphaMultiplier = 1f)
    {
        drawnTop = footprintTopLeft.Y;
        if (visual.Sprite is not { } sprite)
        {
            return SpriteOrGlyphRenderer.Draw(spriteBatch, _spriteSheetService, _labelRenderer, (SpriteView?)null, font, visual.Glyph, visual.IsDead ? Color.Gray : visual.GlyphColor, footprintTopLeft, footprintSize, Color.White, alphaMultiplier, outline: true);
        }

        var (drawnTopLeft, drawnSize) = footprintTiles is { } tiles
            ? SpritePlacementMath.DrawnRectangle(footprintTopLeft, footprintSize, tiles, new Point(sprite.SourceRectangle.Width, sprite.SourceRectangle.Height), sprite.Placement)
            : (footprintTopLeft, footprintSize);
        drawnTop = drawnTopLeft.Y;
        return SpriteOrGlyphRenderer.Draw(spriteBatch, _spriteSheetService, _labelRenderer, sprite, font, string.Empty, Color.White, drawnTopLeft, drawnSize, visual.IsDead ? Color.Gray : Color.White, alphaMultiplier, outline: true);
    }

    /// <summary>One Tiny occupant in the tile's 3x3 sub-grid, at the cell for how many of the tile's Tiny occupants were drawn before it. Returns whether anything was drawn, so an occupant with no visual doesn't take a cell.</summary>
    private bool DrawTinyOccupant(SpriteBatch spriteBatch, int entityId, Vector2 tileOrigin, Point tileSize, int drawnBefore)
    {
        var subCellSize = new Point(tileSize.X / TinyGridDimension, tileSize.Y / TinyGridDimension);
        var subColumn = drawnBefore % TinyGridDimension;
        var subRow = drawnBefore / TinyGridDimension;
        var subCellTopLeft = new Vector2(tileOrigin.X + subColumn * subCellSize.X, tileOrigin.Y + subRow * subCellSize.Y);

        return TryDrawEntityVisual(spriteBatch, entityId, _tinyFont, subCellTopLeft, new Vector2(subCellSize.X, subCellSize.Y), new Vector2Byte(1, 1));
    }

    /// <summary>A Blocking occupant, its health bar and loot badge, its charging badge and its dodge glow. The health bar and charging badge follow the drawn sprite (a tall sprite's head would otherwise cover them); the loot badge and dodge glow are about the footprint and stay on it.</summary>
    private void DrawPrimaryOccupant(SpriteBatch spriteBatch, Texture2D unitRectangle, int entityId, Vector2Byte size, Vector2 footprintTopLeft, Vector2 footprintSize, Point tileSize)
    {
        var status = _mapView.GetStatus(entityId);

        TryDrawEntityVisual(spriteBatch, entityId, FontForSize(size.X), footprintTopLeft, footprintSize, size, out var drawnTop);
        var cueTop = SpritePlacementMath.CueTop(drawnTop, footprintTopLeft.Y);
        DrawEntityIcons(spriteBatch, unitRectangle, entityId, status, footprintTopLeft, footprintSize, tileSize, cueTop);

        // Unlike DrawEntityIcons (health bar/loot badge), the charging badge must show for the
        // player too -- drawn here, directly, rather than inside DrawEntityIcons' own early
        // player-skip guard.
        DrawChargingBadge(spriteBatch, entityId, footprintTopLeft, footprintSize, tileSize, cueTop);

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
            GlowRenderer.Draw(spriteBatch, unitRectangle, new Rectangle((int)footprintTopLeft.X, (int)footprintTopLeft.Y, (int)footprintSize.X, (int)footprintSize.Y), DodgingGlowColor, GlowMode.InteriorFade, DodgingGlowAlphaMultiplier);
        }
    }

    /// <summary>
    /// The LootBag-Red badge, shared by a corpse (DrawOccupants) and a live, still-Blocking
    /// container (DrawEntityIcons -- lootable while alive, unlike a corpse). Nothing when the
    /// entity carries no items; full color if its loot window has never been opened, grey once it
    /// has. Small and single-tile-sized, anchored to the top-right corner of the entity's own full
    /// footprint rather than its origin tile, so a multi-tile corpse gets its badge on its actual
    /// top-right tile.
    /// </summary>
    private void DrawLootBagBadge(SpriteBatch spriteBatch, LootBagState lootBag, Vector2 footprintTopLeft, Vector2 footprintSize, Point tileSize)
    {
        if (lootBag == LootBagState.None || _lootBagSprite is not { } lootBagSprite)
        {
            return;
        }

        var tint = lootBag == LootBagState.Looted ? Color.Gray : Color.White;
        var badgeSize = new Vector2(tileSize.X, tileSize.Y) * LootBagBadgeSizeFraction;
        var badgePosition = new Vector2(footprintTopLeft.X + footprintSize.X - badgeSize.X, footprintTopLeft.Y);

        SpriteOrGlyphRenderer.Draw(spriteBatch, _spriteSheetService, _labelRenderer, lootBagSprite, _badgeFont, string.Empty, tint, badgePosition, badgeSize, tint, outline: true);
    }

    /// <summary>
    /// While entityId is mid-windup on a Delayed action, draws that action's own sprite/glyph as a small badge centered
    /// above its footprint -- "put that action/item's sprite as a badge above their sprite on the map" (Combat Overhaul:
    /// Dodge, TODO.md). Positioned a full badge-height above cueTop -- the top of the drawn sprite, or the footprint's if
    /// that is higher -- so it never collides with DrawHealthBar's own bar, which sits inside the footprint's top edge.
    /// Called directly from DrawPrimaryOccupant, not from DrawEntityIcons, since the player must see their own charging
    /// badge too and DrawEntityIcons deliberately skips the player entirely.
    /// </summary>
    private void DrawChargingBadge(SpriteBatch spriteBatch, int entityId, Vector2 footprintTopLeft, Vector2 footprintSize, Point tileSize, float cueTop)
    {
        if (!_mapView.TryGetChargingAction(entityId, out var action))
        {
            return;
        }

        var badgeSize = new Vector2(tileSize.X, tileSize.Y) * ChargingBadgeSizeFraction;
        var badgePosition = new Vector2(footprintTopLeft.X + (footprintSize.X - badgeSize.X) / 2f, cueTop - badgeSize.Y);

        SpriteView? actionSprite = action.SpriteName is { } spriteName && _spriteSheetService.Sprites.TryGetFirst(spriteName, out var icon) ? icon : null;
        SpriteOrGlyphRenderer.Draw(spriteBatch, _spriteSheetService, _labelRenderer, actionSprite, _badgeFont, action.Glyph, action.GlyphColor, badgePosition, badgeSize, Color.White, outline: true);
    }

    private void DrawEntityIcons(SpriteBatch spriteBatch, Texture2D unitRectangle, int entityId, EntityStatusView status, Vector2 footprintTopLeft, Vector2 footprintSize, Point tileSize, float cueTop)
    {
        if (entityId == _mapView.PlayerEntityId)
        {
            return;
        }

        if (status.HealthFraction is { } healthFraction)
        {
            DrawHealthBar(spriteBatch, unitRectangle, healthFraction, footprintTopLeft, footprintSize, cueTop);
        }

        // A container is lootable while alive, unlike a creature (only lootable once dead, see
        // DrawOccupants' corpse call to the same badge helper) -- gated on IsContainer, not
        // just "carries items," since a live goblin/fairy/player also carries its own inventory
        // stacks and must not show a loot-bag badge for those.
        if (status.IsContainer)
        {
            DrawLootBagBadge(spriteBatch, status.LootBag, footprintTopLeft, footprintSize, tileSize);
        }
    }

    /// <summary>Thin bar at cueTop -- the top of the drawn sprite, or of the footprint if that is higher -- only drawn below full health (EntityStatusView.HealthFraction is null otherwise). Black backdrop doubles as the outline and the "missing health" portion; the fill rect insets 1px and its width (not the outline's) scales with the health fraction.</summary>
    private static void DrawHealthBar(SpriteBatch spriteBatch, Texture2D unitRectangle, float healthFraction, Vector2 footprintTopLeft, Vector2 footprintSize, float cueTop)
    {
        var barWidth = footprintSize.X * HealthBarWidthFraction;
        var barX = footprintTopLeft.X + (footprintSize.X - barWidth) / 2f;
        var barY = cueTop;

        var outerRectangle = new Rectangle((int)barX, (int)barY, (int)barWidth, HealthBarHeightPixels);
        spriteBatch.Draw(unitRectangle, outerRectangle, HealthBarPalette.OutlineColor);

        var innerWidth = (int)((outerRectangle.Width - 2) * healthFraction);
        if (innerWidth > 0)
        {
            spriteBatch.Draw(unitRectangle, new Rectangle(outerRectangle.X + 1, outerRectangle.Y + 1, innerWidth, HealthBarHeightPixels - 2), HealthBarPalette.FractionColor(healthFraction));
        }
    }

    /// <summary>Medium/large/huge glyph font by an entity's footprint width.</summary>
    private SpriteFontBase FontForSize(int sizeX) => sizeX switch
    {
        1 => _mediumFont,
        2 => _largeFont,
        _ => _hugeFont,
    };

    /// <summary>
    /// Blue up-arrow (top-right) if any layer above the current one is occupied; brown
    /// down-arrow (bottom-right) if any layer below is. A tile-level badge -- unlike
    /// DrawEntityIcons, this describes the tile's other layers, not the Blocking occupant
    /// drawn on it -- so MapDrawOrder plans every badge after every occupant, where no later
    /// row's tall sprite can paint over it.
    /// </summary>
    private void DrawLayerBadges(SpriteBatch spriteBatch, bool hasHigherLayer, bool hasLowerLayer, Vector2 tileOrigin, Point tileSize)
    {
        if (hasHigherLayer)
        {
            var drawPosition = new Vector2(tileOrigin.X + tileSize.X - _badgeFont.LineHeight, tileOrigin.Y);
            _labelRenderer.Draw(spriteBatch, _badgeFont, "^", drawPosition, UpLayerBadgeColor, outline: true);
        }

        if (hasLowerLayer)
        {
            var drawPosition = new Vector2(tileOrigin.X + tileSize.X - _badgeFont.LineHeight, tileOrigin.Y + tileSize.Y - _badgeFont.LineHeight);
            _labelRenderer.Draw(spriteBatch, _badgeFont, "v", drawPosition, DownLayerBadgeColor, outline: true);
        }
    }
}
