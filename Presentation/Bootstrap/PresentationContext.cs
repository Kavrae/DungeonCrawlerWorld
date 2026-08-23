using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Presentation.Bootstrap;

/// <summary>Bundles the constructed Presentation services, produced by PresentationBootstrapper.</summary>
public sealed class PresentationContext(
    FontService fontService,
    SpriteBatchRenderer spriteBatchRenderer,
    LabelRenderer labelRenderer,
    TileRenderer tileRenderer,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    ElementPoolService elementPoolService)
{
    public FontService FontService { get; } = fontService ?? throw new ArgumentNullException(nameof(fontService));
    public SpriteBatchRenderer SpriteBatchRenderer { get; } = spriteBatchRenderer ?? throw new ArgumentNullException(nameof(spriteBatchRenderer));
    public LabelRenderer LabelRenderer { get; } = labelRenderer ?? throw new ArgumentNullException(nameof(labelRenderer));
    public TileRenderer TileRenderer { get; } = tileRenderer ?? throw new ArgumentNullException(nameof(tileRenderer));
    public SpriteSheetService SpriteSheetService { get; } = spriteSheetService ?? throw new ArgumentNullException(nameof(spriteSheetService));
    public SpriteRenderer SpriteRenderer { get; } = spriteRenderer ?? throw new ArgumentNullException(nameof(spriteRenderer));
    public ElementPoolService ElementPoolService { get; } = elementPoolService ?? throw new ArgumentNullException(nameof(elementPoolService));

    /// <summary>
    /// Captures the render services ElementPoolService needs for every Element's Draw/DrawContent/
    /// DrawHeader (see ElementPoolService.Initialize's own doc comment) -- called once, from
    /// GameLoop.LoadContent, after GraphicsDevice/unitRectangle both exist. Sources spriteBatch
    /// from this context's own SpriteBatchRenderer rather than taking it as a parameter too, since
    /// PresentationContext already owns that reference -- GameLoop only needs to cross into
    /// Presentation once, through this one call, not reach into ElementPoolService directly.
    /// </summary>
    public void LoadContent(GraphicsDevice graphicsDevice, Texture2D unitRectangle) =>
        ElementPoolService.Initialize(graphicsDevice, SpriteBatchRenderer.GetSpriteBatch(), unitRectangle);
}