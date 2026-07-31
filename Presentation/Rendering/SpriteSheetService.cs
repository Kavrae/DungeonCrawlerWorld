using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>
/// Loads and caches spritesheet textures by relative path. Mirrors FontService's directory/
/// AppContext.BaseDirectory resolution convention. Construction does no I/O and never touches
/// graphicsDevice -- both happen lazily inside GetTexture, on first request for a given path --
/// so this type is safe to construct in headless tests that never call GetTexture.
/// </summary>
public sealed class SpriteSheetService
{
    private readonly GraphicsDevice? _graphicsDevice;
    private readonly string _spritesheetsDirectory;
    private readonly Dictionary<string, Texture2D> _textures = [];

    /// <param name="graphicsDevice">
    /// Nullable so this type can be constructed in headless tests that never call GetTexture --
    /// real callers (PresentationBootstrapper.Build) always pass a real device and always
    /// reach a real GetTexture call, so the null case only ever exercises the constructor.
    /// </param>
    /// <param name="spritesheetsDirectory">
    /// Directory containing spritesheet files, relative to the exe's output directory.
    /// Resolved against <see cref="AppContext.BaseDirectory"/> rather than the process's
    /// current working directory, same as FontService's fontsDirectory.
    /// </param>
    public SpriteSheetService(GraphicsDevice? graphicsDevice, string spritesheetsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spritesheetsDirectory);

        _graphicsDevice = graphicsDevice;
        _spritesheetsDirectory = spritesheetsDirectory;
    }

    public Texture2D GetTexture(string relativePath)
    {
        if (_textures.TryGetValue(relativePath, out var cached))
        {
            return cached;
        }

        ArgumentNullException.ThrowIfNull(_graphicsDevice);

        var resolvedPath = Path.Combine(AppContext.BaseDirectory, _spritesheetsDirectory, relativePath);
        using var stream = File.OpenRead(resolvedPath);
        var texture = Texture2D.FromStream(_graphicsDevice, stream);
        _textures[relativePath] = texture;
        return texture;
    }
}
