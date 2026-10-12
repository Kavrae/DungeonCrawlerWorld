using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>
/// Where sprites come from: the manifest that turns a sprite name into its cell (Sprites), and
/// the spritesheet textures those cells are cut from, loaded and cached by relative path.
/// </summary>
/// <remarks>
/// Mirrors FontService's directory/AppContext.BaseDirectory resolution convention. Construction
/// does no texture I/O and never touches graphicsDevice -- both happen lazily inside GetTexture,
/// on first request for a given path -- so this type is safe to construct in headless tests that
/// never call GetTexture.
/// </remarks>
public sealed class SpriteSheetService
{
    private readonly GraphicsDevice? _graphicsDevice;
    private readonly string _spritesheetsDirectory;
    private readonly Dictionary<string, Texture2D> _textures = new(ReferenceEqualityComparer.Instance);

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
    /// <param name="sprites">The loaded sprite manifest every sprite name is looked up in.</param>
    public SpriteSheetService(GraphicsDevice? graphicsDevice, string spritesheetsDirectory, SpriteManifestResolver sprites)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spritesheetsDirectory);

        _graphicsDevice = graphicsDevice;
        _spritesheetsDirectory = spritesheetsDirectory;
        Sprites = sprites;
    }

    /// <summary>The loaded sprite manifest: a sprite name to the cell it draws as.</summary>
    public SpriteManifestResolver Sprites { get; }

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
