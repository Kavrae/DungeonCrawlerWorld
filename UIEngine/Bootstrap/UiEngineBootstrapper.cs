using Microsoft.Xna.Framework.Graphics;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace UIEngine.Bootstrap;

/// <summary>
/// Constructs the window framework's services: fonts, the sprite batch, label drawing, the sprite manifest and its sheets, and
/// the element pool. Deliberately not a copy of Engine's module/dependency-validating Bootstrapper -- the service set is
/// small and fixed, so it doesn't need that machinery. New window/control types register with
/// ElementPoolService.RegisterFactory, not a second module system.
/// </summary>
public static class UiEngineBootstrapper
{
    /// <param name="spriteManifestFileName">The sprite manifest, relative to the exe's output directory; loaded and checked here, so a broken file stops startup naming its entry.</param>
    public static UiEngineContext Build(GraphicsDevice graphicsDevice, string fontsDirectory, string spritesheetsDirectory, string spriteManifestFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(spritesheetsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(spriteManifestFileName);

        var fontService = new FontService(fontsDirectory);
        var spriteBatchRenderer = new SpriteBatchRenderer(graphicsDevice);
        var labelRenderer = new LabelRenderer();
        var sprites = SpriteManifestResolver.Load(Path.Combine(AppContext.BaseDirectory, spriteManifestFileName));
        var spriteSheetService = new SpriteSheetService(graphicsDevice, spritesheetsDirectory, sprites);
        var elementPoolService = new ElementPoolService();

        return new UiEngineContext(fontService, spriteBatchRenderer, labelRenderer, spriteSheetService, elementPoolService);
    }
}