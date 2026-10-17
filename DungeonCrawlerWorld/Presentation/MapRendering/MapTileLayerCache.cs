using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.MapRendering;

/// <summary>
/// One full-viewport pass of MapRenderer's draw order, rendered into an off-screen texture and
/// blitted as a single draw call per frame instead of being re-submitted tile-by-tile.
/// </summary>
/// <remarks>
/// Used for the two passes whose content only changes on an explicit event rather than
/// continuously: the tile backgrounds plus terrain (blitted under everything) and the aura glow
/// overlay (blitted over the occupants, which is the whole reason it can't simply be folded into
/// the terrain texture -- see MapRenderer.DrawGlowOverlay's own note on why the glow has to land on
/// top of an opaque occupant sprite rather than beneath it).
///
/// Both qualify for the same reason. Terrain only changes through World.SetTerrain, which raises
/// TerrainChangedEvent, and its visuals and background come from its TerrainDefinition -- so the
/// background wash is a pure function of terrain too. The
/// glow grid is incrementally maintained and only changes when the aura field adds or
/// removes a source, which at this game's real composition means essentially never: its sources
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
/// contain, and that changes the image's key (MapImageKey). Rendering happens
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
    private MapImageKey _renderedKey;

    public MapTileLayerCache(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
    }

    /// <summary>
    /// Marks the cached image stale so the next EnsureRendered call rebuilds it, for a change the image's key can't
    /// see -- the terrain itself for the terrain image, an AuraPowerView version change for the glow one.
    /// </summary>
    public void Invalidate() => _isDirty = true;

    /// <summary>
    /// Rebuilds the cached texture if it was invalidated or was rendered for a different key. Must be called outside
    /// any active SpriteBatch pass -- see this class's own remarks. Does nothing at all on a frame where the image is
    /// still valid, which is the overwhelming majority of them.
    /// </summary>
    /// <param name="spriteBatch">The batch to render the tiles with -- begun and ended internally, so it must not already be active.</param>
    /// <param name="key">What the image should show now.</param>
    /// <param name="drawTiles">Renders the grid key describes at whole-tile positions with no sub-tile offset -- the renderer supplies the pass itself rather than this type knowing anything about the map.</param>
    public void EnsureRendered(SpriteBatch spriteBatch, MapImageKey key, Action<SpriteBatch, MapImageKey> drawTiles)
    {
        if (key.TileColumns <= 0 || key.TileRows <= 0 || key.TileSize.X <= 0 || key.TileSize.Y <= 0)
        {
            return;
        }

        // IsContentLost covers a device reset (alt-tab from fullscreen, driver recovery) silently
        // discarding the texture behind our back -- without it the map would render as whatever
        // garbage the reclaimed surface held, with no invalidation ever raised to explain why.
        if (!_isDirty && key == _renderedKey && _target is { IsDisposed: false, IsContentLost: false })
        {
            return;
        }

        var gridChanged = key.TileColumns != _renderedKey.TileColumns || key.TileRows != _renderedKey.TileRows || key.TileSize != _renderedKey.TileSize;
        if (gridChanged || _target is null || _target.IsDisposed)
        {
            _target?.Dispose();

            // PreserveContents, not the DiscardContents default: this texture's whole purpose is
            // to survive untouched across the many frames between rebuilds.
            _target = new RenderTarget2D(
                _graphicsDevice,
                key.TileColumns * key.TileSize.X,
                key.TileRows * key.TileSize.Y,
                mipMap: false,
                SurfaceFormat.Color,
                DepthFormat.None,
                preferredMultiSampleCount: 0,
                RenderTargetUsage.PreserveContents);
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
        drawTiles(spriteBatch, key);
        spriteBatch.End();

        if (previousTargets is { Length: > 0 })
        {
            _graphicsDevice.SetRenderTargets(previousTargets);
        }
        else
        {
            _graphicsDevice.SetRenderTarget(null);
        }

        _renderedKey = key;
        _isDirty = false;
    }

    /// <summary>
    /// Blits the cached image. position is where the grid's own (0,0) tile corner belongs on
    /// screen -- the renderer passes -RenderPixelOffset, reproducing exactly the shift every tile
    /// used to apply individually.
    /// </summary>
    /// <param name="key">What the image must show; an image rendered for anything else isn't drawn.</param>
    /// <returns>Whether anything was drawn -- false before the first successful render, and when the camera has moved since the image was rendered, so the caller can draw the tiles directly rather than show a stale or empty viewport for a frame.</returns>
    public bool Draw(SpriteBatch spriteBatch, MapImageKey key, Vector2 position)
    {
        if (_target is null || _target.IsDisposed || _isDirty || key != _renderedKey)
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
