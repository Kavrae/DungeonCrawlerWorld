using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Presentation.Bootstrap;

/// <summary>Bundles the constructed Presentation services, produced by PresentationBootstrapper.</summary>
public sealed class PresentationContext
{
    public FontService FontService { get; }
    public SpriteBatchRenderer SpriteBatchRenderer { get; }
    public GlyphRenderer GlyphRenderer { get; }
    public TileRenderer TileRenderer { get; }
    public WindowService WindowService { get; }

    public PresentationContext(
        FontService fontService,
        SpriteBatchRenderer spriteBatchRenderer,
        GlyphRenderer glyphRenderer,
        TileRenderer tileRenderer,
        WindowService windowService)
    {
        FontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
        SpriteBatchRenderer = spriteBatchRenderer ?? throw new ArgumentNullException(nameof(spriteBatchRenderer));
        GlyphRenderer = glyphRenderer ?? throw new ArgumentNullException(nameof(glyphRenderer));
        TileRenderer = tileRenderer ?? throw new ArgumentNullException(nameof(tileRenderer));
        WindowService = windowService ?? throw new ArgumentNullException(nameof(windowService));
    }
}
