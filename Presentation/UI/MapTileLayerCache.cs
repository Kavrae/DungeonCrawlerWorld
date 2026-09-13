using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.UI;

/// <summary>
/// One full-viewport pass of MapWindow's draw order, rendered into an off-screen texture and
/// blitted as a single draw call per frame instead of being re-submitted tile-by-tile.
/// </summary>
/// <remarks>
/// Used for the two passes whose content only changes on an explicit event rather than
/// continuously: the tile backgrounds plus terrain (blitted under everything) and the aura glow
/// overlay (blitted over the occupants, which is the whole reason it can't simply be folded into
/// the terrain texture -- see MapWindow.DrawGlowOverlay's own note on why the glow has to land on
/// top of an opaque occupant sprite rather than beneath it).
///
/// Both qualify for the same reason. Terrain is static: Map.SetTerrainEntityId has one caller
/// (World.PlaceTerrainOnMap, which now raises TerrainChangedEvent), nothing mutates a terrain
/// entity's SpriteComponent/GlyphComponent at runtime, and BackgroundComponent only ever appears
/// on terrain blueprints -- so MapBackgroundCache's "the Blocking occupant's background wins"
/// branch never actually fires and the background wash is a pure function of terrain too. The
/// glow grid is incrementally maintained and only changes when MapTintGrid actually splats or
/// unsplats a source, which at this game's real composition means essentially never: its sources
/// are overwhelmingly Lava terrain, which never moves.
///
/// Measured motivation: the terrain pass cost 15.8ms of a 1000ms/sec budget, the background pass
/// 2.6ms and the glow overlay 6.5ms -- together roughly a quarter of MapWindow's entire draw
/// time, re-deriving images that had not changed. Most of that was not quad submission but the
/// per-tile lookups behind it: three entity-indexed pool reads per terrain tile, a hash lookup
/// per glow tile, and the glyph-only terrain on top of that -- Lava and StoneFloor have no sprite
/// in SpriteManifest, and every outlined glyph is nine DrawString calls (see
/// ContrastTextRenderer), so a lava-heavy or wall-heavy viewport paid that nine times over on
/// ~10% of its tiles.
///
/// Rendered with no sub-tile offset and blitted at -RenderPixelOffset, so a right-drag's smooth
/// scroll costs nothing extra: only a whole-tile scroll actually changes what a texture should
/// contain, and that already goes through MapWindow's own invalidation points. Rendering happens
/// during Update, not Draw, because Draw runs inside GameLoop's already-active SpriteBatch pass
/// and swapping render targets mid-pass would mean unwinding and restoring ambient state that
/// ElementPoolService's Push/PopRenderState stack deliberately owns.
///
/// One tile of overscan on each axis: MapCamera already sizes its grid to contentSize/tileSize
/// + 2 for exactly this reason, and the blit shifts by up to one tile, so the texture has to
/// cover the shifted footprint without exposing an unpainted edge.
/// </remarks>
public sealed class MapTileLayerCache : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;

    private RenderTarget2D? _target;
    private bool _isDirty = true;
    private int _renderedTileColumns;
    private int _renderedTileRows;
    private Point _renderedTileSize;

    public MapTileLayerCache(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        _graphicsDevice = graphicsDevice;
    }

    /// <summary>
    /// Marks the cached image stale so the next EnsureRendered call rebuilds it. Called from
    /// MapWindow's existing camera-commit points (scroll, zoom, layer change) plus whatever
    /// content event the particular pass depends on -- TerrainChangedEvent for the terrain
    /// texture, a MapTintGrid version change for the glow one.
    /// </summary>
    public void Invalidate() => _isDirty = true;

    /// <summary>
    /// Rebuilds the cached texture if it is stale, or if the camera's tile grid no longer
    /// matches what was rendered. Must be called outside any active SpriteBatch pass -- see this
    /// class's own remarks. Does nothing at all on a frame where the image is still valid, which
    /// is the overwhelming majority of them.
    /// </summary>
    /// <param name="spriteBatch">The batch to render the tiles with -- begun and ended internally, so it must not already be active.</param>
    /// <param name="tileColumns">The camera's current visible tile column count.</param>
    /// <param name="tileRows">The camera's current visible tile row count.</param>
    /// <param name="tileSize">The camera's current tile size in pixels.</param>
    /// <param name="drawTiles">Renders the grid at whole-tile positions with no sub-tile offset -- MapWindow supplies the pass itself here rather than this type knowing anything about Map, entities or pools.</param>
    public void EnsureRendered(SpriteBatch spriteBatch, int tileColumns, int tileRows, Point tileSize, Action<SpriteBatch> drawTiles)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);
        ArgumentNullException.ThrowIfNull(drawTiles);

        if (tileColumns <= 0 || tileRows <= 0 || tileSize.X <= 0 || tileSize.Y <= 0)
        {
            return;
        }

        var gridChanged = tileColumns != _renderedTileColumns || tileRows != _renderedTileRows || tileSize != _renderedTileSize;

        // IsContentLost covers a device reset (alt-tab from fullscreen, driver recovery) silently
        // discarding the texture behind our back -- without it the map would render as whatever
        // garbage the reclaimed surface held, with no invalidation ever raised to explain why.
        if (!_isDirty && !gridChanged && _target is { IsDisposed: false, IsContentLost: false })
        {
            return;
        }

        if (gridChanged || _target is null || _target.IsDisposed)
        {
            _target?.Dispose();

            // PreserveContents, not the DiscardContents default: this texture's whole purpose is
            // to survive untouched across the many frames between rebuilds.
            _target = new RenderTarget2D(
                _graphicsDevice,
                tileColumns * tileSize.X,
                tileRows * tileSize.Y,
                mipMap: false,
                SurfaceFormat.Color,
                DepthFormat.None,
                preferredMultiSampleCount: 0,
                RenderTargetUsage.PreserveContents);

            _renderedTileColumns = tileColumns;
            _renderedTileRows = tileRows;
            _renderedTileSize = tileSize;
        }

        // Captured and restored rather than assuming the backbuffer was bound: this runs during
        // Update, where nothing else sets a target today, but restoring what was actually there
        // costs nothing and keeps this from silently stealing the target from a future caller.
        // The empty case is handled explicitly -- SetRenderTargets with a zero-length array is not
        // a documented way to rebind the backbuffer, whereas SetRenderTarget(null) is.
        var previousTargets = _graphicsDevice.GetRenderTargets();

        _graphicsDevice.SetRenderTarget(_target);
        _graphicsDevice.Clear(Color.Transparent);

        // Mirrors SpriteBatchRenderer.StartSpriteBatch's own parameters -- PointClamp in
        // particular, since every glyph here comes from a font atlas that crops under linear
        // sampling (see LabelRenderer's own note on point sampling and fractional positions).
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, Matrix.Identity);
        drawTiles(spriteBatch);
        spriteBatch.End();

        if (previousTargets is { Length: > 0 })
        {
            _graphicsDevice.SetRenderTargets(previousTargets);
        }
        else
        {
            _graphicsDevice.SetRenderTarget(null);
        }

        _isDirty = false;
    }

    /// <summary>
    /// Blits the cached image. position is where the grid's own (0,0) tile corner belongs on
    /// screen -- MapWindow passes -RenderPixelOffset, reproducing exactly the shift every tile
    /// used to apply individually.
    /// </summary>
    /// <returns>Whether anything was drawn -- false before the first successful render, so the caller can fall back to drawing the tiles directly rather than showing an empty viewport for a frame.</returns>
    public bool Draw(SpriteBatch spriteBatch, Vector2 position)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);

        if (_target is null || _target.IsDisposed || _isDirty)
        {
            return false;
        }

        spriteBatch.Draw(_target, position, Color.White);
        return true;
    }

    public void Dispose()
    {
        _target?.Dispose();
        _target = null;
    }
}
