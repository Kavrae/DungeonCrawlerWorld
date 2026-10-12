using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Presentation.Bootstrap;

/// <summary>
/// Constructs Presentation's services. Deliberately not a copy of Engine's module/
/// dependency-validating Bootstrapper -- Presentation's service set is small and fixed
/// (Font/SpriteBatch/Window), so it doesn't need that machinery. If new window/control
/// types need registering later, that's WindowService.RegisterFactory, not a second
/// module system.
/// </summary>
public static class PresentationBootstrapper
{
    /// <param name="spriteManifestFileName">The sprite manifest, relative to the exe's output directory; loaded and checked here, so a broken file stops startup naming its entry.</param>
    public static PresentationContext Build(GraphicsDevice graphicsDevice, string fontsDirectory, string spritesheetsDirectory, string spriteManifestFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(spritesheetsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(spriteManifestFileName);

        var fontService = new FontService(fontsDirectory);
        var spriteBatchRenderer = new SpriteBatchRenderer(graphicsDevice);
        var labelRenderer = new LabelRenderer();
        var sprites = SpriteManifestResolver.Load(Path.Combine(AppContext.BaseDirectory, spriteManifestFileName));
        var spriteSheetService = new SpriteSheetService(graphicsDevice, spritesheetsDirectory, sprites);
        var spriteRenderer = new SpriteRenderer();
        var elementPoolService = new ElementPoolService();

        return new PresentationContext(fontService, spriteBatchRenderer, labelRenderer, spriteSheetService, spriteRenderer, elementPoolService);
    }
}