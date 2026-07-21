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
    public static PresentationContext Build(GraphicsDevice graphicsDevice, string fontsDirectory)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontsDirectory);

        var fontService = new FontService(fontsDirectory);
        var spriteBatchRenderer = new SpriteBatchRenderer(graphicsDevice);
        var glyphRenderer = new GlyphRenderer();
        var tileRenderer = new TileRenderer();
        var windowService = new WindowService(fontService, glyphRenderer);

        return new PresentationContext(fontService, spriteBatchRenderer, glyphRenderer, tileRenderer, windowService);
    }
}
