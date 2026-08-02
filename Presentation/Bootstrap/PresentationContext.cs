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
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    ElementPoolService elementPoolService)
{
    public FontService FontService { get; } = fontService ?? throw new ArgumentNullException(nameof(fontService));
    public SpriteBatchRenderer SpriteBatchRenderer { get; } = spriteBatchRenderer ?? throw new ArgumentNullException(nameof(spriteBatchRenderer));
    public GlyphRenderer GlyphRenderer { get; } = glyphRenderer ?? throw new ArgumentNullException(nameof(glyphRenderer));
    public TileRenderer TileRenderer { get; } = tileRenderer ?? throw new ArgumentNullException(nameof(tileRenderer));
    public SpriteSheetService SpriteSheetService { get; } = spriteSheetService ?? throw new ArgumentNullException(nameof(spriteSheetService));
    public SpriteRenderer SpriteRenderer { get; } = spriteRenderer ?? throw new ArgumentNullException(nameof(spriteRenderer));
    public ElementPoolService ElementPoolService { get; } = elementPoolService ?? throw new ArgumentNullException(nameof(elementPoolService));
}