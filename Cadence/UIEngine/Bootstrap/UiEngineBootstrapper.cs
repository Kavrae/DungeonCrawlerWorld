using Microsoft.Xna.Framework.Graphics;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace UIEngine.Bootstrap;

/// <summary>
/// Constructs the window framework's services: fonts, the sprite batch, the sprite effect and its batch, the render service
/// they are drawn through, label drawing, the sprite manifest and its sheets, and the element pool. Deliberately not a copy of Engine's module/dependency-validating Bootstrapper -- the service set is
/// small and fixed, so it doesn't need that machinery. UiEngineContext.RegisterFrameworkElementFactories registers
/// UIEngine's own controls; a host's own types register with ElementPoolService.RegisterFactory, not a second module system.
/// </summary>
public static class UiEngineBootstrapper
{
    /// <param name="spriteManifestFileName">The sprite manifest, relative to the exe's output directory; loaded and checked here, so a broken file stops startup naming its entry.</param>
    /// <param name="effectsDirectory">Content effects, relative to the exe's output directory (SpriteEffectService); need not exist. A refused override of the sprite effect is reported to Console.Error.</param>
    public static UiEngineContext Build(GraphicsDevice graphicsDevice, string fontsDirectory, string spritesheetsDirectory, string spriteManifestFileName, string effectsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(spritesheetsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(spriteManifestFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectsDirectory);

        var fontService = new FontService(fontsDirectory);
        var spriteBatchRenderer = new SpriteBatchRenderer(graphicsDevice);
        var spriteEffectService = new SpriteEffectService(graphicsDevice, effectsDirectory, Console.Error);
        var spriteEffectBatch = new SpriteEffectBatch(graphicsDevice, spriteEffectService.SpriteEffect);
        var sprites = SpriteManifestResolver.Load(Path.Combine(AppContext.BaseDirectory, spriteManifestFileName));
        var spriteSheetService = new SpriteSheetService(graphicsDevice, spritesheetsDirectory, sprites);
        var renderService = new UiRenderService();
        var elementPoolService = new ElementPoolService();

        return new UiEngineContext(fontService, spriteBatchRenderer, spriteEffectService, spriteEffectBatch, renderService, spriteSheetService, elementPoolService);
    }
}