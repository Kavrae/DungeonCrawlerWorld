using Microsoft.Xna.Framework.Graphics;

namespace Presentation.MapRendering;

/// <summary>MapGlowTexels as the texture the sprite effect reads, rebuilt when the grid or the auras change since it was last built.</summary>
/// <remarks>Stale when the grid differs from what it was built for (MapImageKey), when AuraPowerView.Version has moved, or when told (Invalidate) -- see MapBuildStamp.</remarks>
public sealed class MapGlowTexture(MapGlowTexels texels) : IDisposable
{
    private readonly MapBuildStamp _built = new();
    private Texture2D? _texture;

    /// <summary>Marks the texture stale for a change the key and the aura version can't see -- the terrain itself.</summary>
    public void Invalidate() => _built.Invalidate();

    /// <summary>The texture for the grid key describes, rebuilt first when it is stale; resized with the grid.</summary>
    public Texture2D Ensure(GraphicsDevice graphicsDevice, MapImageKey key, int auraVersion)
    {
        if (_texture is { } current && _built.IsCurrentFor(key, auraVersion))
        {
            return current;
        }

        texels.Build(key);
        if (_texture is null || _texture.Width != texels.Width || _texture.Height != texels.Height)
        {
            _texture?.Dispose();
            _texture = new Texture2D(graphicsDevice, texels.Width, texels.Height);
        }

        _texture.SetData(texels.Texels, 0, texels.Width * texels.Height);
        _built.Record(key, auraVersion);
        return _texture;
    }

    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        _built.Invalidate();
    }
}
