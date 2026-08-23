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
    public static PresentationContext Build(GraphicsDevice graphicsDevice, string fontsDirectory, string spritesheetsDirectory)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(spritesheetsDirectory);

        var fontService = new FontService(fontsDirectory);
        var spriteBatchRenderer = new SpriteBatchRenderer(graphicsDevice);
        var labelRenderer = new LabelRenderer();
        var tileRenderer = new TileRenderer();
        var spriteSheetService = new SpriteSheetService(graphicsDevice, spritesheetsDirectory);
        var spriteRenderer = new SpriteRenderer();
        var elementPoolService = new ElementPoolService();

        return new PresentationContext(fontService, spriteBatchRenderer, labelRenderer, tileRenderer, spriteSheetService, spriteRenderer, elementPoolService);
    }
}