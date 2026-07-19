using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>
/// Owns the shared SpriteBatch instance -- one shared batch is safer than each window or
/// system creating its own, and less boilerplate than passing GraphicsDevice around.
/// </summary>
public sealed class SpriteBatchRenderer
{
    private readonly SpriteBatch _spriteBatch;

    public SpriteBatchRenderer(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        _spriteBatch = new SpriteBatch(graphicsDevice);
    }

    public SpriteBatch StartSpriteBatch()
    {
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        return _spriteBatch;
    }

    public SpriteBatch GetSpriteBatch() => _spriteBatch;

    public void EndSpriteBatch() => _spriteBatch.End();
}
