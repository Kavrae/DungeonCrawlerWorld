using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UIEngine.Rendering;

namespace Presentation.MapRendering;

/// <summary>
/// One full-viewport pass of MapRenderer's draw order, rendered into an off-screen texture and
/// blitted as a single draw call per frame instead of being re-submitted tile-by-tile.
/// </summary>
/// <remarks>
/// Used for the passes whose content only changes on an explicit event rather than continuously:
/// the ground (floors and the cached walls' faces, blitted under everything and lit by the glow as
/// it is blitted) and the cached walls' tops (a second image, because tops are never lit). Terrain
/// only changes through World.SetTerrain, which raises TerrainChangedEvent, and its visuals come
/// from its TerrainDefinition -- so both images are a pure function of terrain.
///
/// Measured motivation: the terrain pass cost 15.8ms of a 1000ms/sec budget, the background pass
/// 2.6ms and the glow overlay (a cached image too, until the glow became a texture the sprite
/// effect reads) 6.5ms -- together roughly a quarter of MapWindow's entire draw
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
/// during Update, not Draw, because Draw runs inside GameLoop's already-active UI pass
/// and swapping render targets mid-pass would mean unwinding and restoring ambient state that
/// UiRenderService's Push/PopRenderState stack deliberately owns.
///
/// One tile of overscan on each axis: MapCamera already sizes its grid to contentSize/tileSize
/// + 2 for exactly this reason, and the blit shifts by up to one tile, so the texture has to
/// cover the shifted footprint without exposing an unpainted edge.
/// </remarks>
public sealed class MapTileLayerCache : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Color _backgroundColor;

    private RenderTarget2D? _target;
    private bool _isDirty = true;
    private MapImageKey _renderedKey;

    /// <param name="backgroundColor">What the image holds where nothing is drawn: the ground's flat fill, so the blit covers it and nothing is drawn beneath, or Transparent for an image drawn over others.</param>
    public MapTileLayerCache(GraphicsDevice graphicsDevice, Color backgroundColor)
    {
        _graphicsDevice = graphicsDevice;
        _backgroundColor = backgroundColor;
    }

    /// <summary>
    /// Marks the cached image stale so the next EnsureRendered call rebuilds it, for a change the image's key can't
    /// see -- the terrain itself.
    /// </summary>
    public void Invalidate() => _isDirty = true;

    /// <summary>
    /// Rebuilds the cached texture if it was invalidated or was rendered for a different key. Must be called outside
    /// any active pass of the batch -- see this class's own remarks. Does nothing at all on a frame where the image is
    /// still valid, which is the overwhelming majority of them.
    /// </summary>
    /// <param name="batch">The batch to render the tiles with -- begun and ended internally, so it must not already be active.</param>
    /// <param name="key">What the image should show now.</param>
    /// <param name="drawTiles">Renders the grid key describes at whole-tile positions with no sub-tile offset -- the renderer supplies the pass itself rather than this type knowing anything about the map.</param>
    public void EnsureRendered(SpriteEffectBatch batch, MapImageKey key, Action<SpriteEffectBatch, MapImageKey> drawTiles)
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
        _graphicsDevice.Clear(_backgroundColor);

        batch.Begin();
        drawTiles(batch, key);
        batch.End();

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
    /// <param name="style">How the sprite effect shades the image as it is blitted -- the ground is lit by the glow here, not when rendered.</param>
    public bool Draw(SpriteEffectBatch batch, MapImageKey key, Vector2 position, SpriteDrawStyle style)
    {
        if (_target is null || _target.IsDisposed || _isDirty || key != _renderedKey)
        {
            return false;
        }

        batch.Draw(_target, position, null, Color.White, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, style);
        return true;
    }

    public void Dispose()
    {
        _target?.Dispose();
        _target = null;
    }
}
