using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Presentation.Bootstrap;

/// <summary>Bundles the constructed Presentation services, produced by PresentationBootstrapper.</summary>
public sealed class PresentationContext(
    FontService fontService,
    SpriteBatchRenderer spriteBatchRenderer,
    GlyphRenderer glyphRenderer,
    TileRenderer tileRenderer,
    WindowService windowService)
{
    public FontService FontService { get; } = fontService ?? throw new ArgumentNullException(nameof(fontService));
    public SpriteBatchRenderer SpriteBatchRenderer { get; } = spriteBatchRenderer ?? throw new ArgumentNullException(nameof(spriteBatchRenderer));
    public GlyphRenderer GlyphRenderer { get; } = glyphRenderer ?? throw new ArgumentNullException(nameof(glyphRenderer));
    public TileRenderer TileRenderer { get; } = tileRenderer ?? throw new ArgumentNullException(nameof(tileRenderer));
    public WindowService WindowService { get; } = windowService ?? throw new ArgumentNullException(nameof(windowService));
}