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
    public FontService FontService { get; } = fontService;
    public SpriteBatchRenderer SpriteBatchRenderer { get; } = spriteBatchRenderer;
    public LabelRenderer LabelRenderer { get; } = labelRenderer;
    public TileRenderer TileRenderer { get; } = tileRenderer;
    public SpriteSheetService SpriteSheetService { get; } = spriteSheetService;
    public SpriteRenderer SpriteRenderer { get; } = spriteRenderer;
    public ElementPoolService ElementPoolService { get; } = elementPoolService;

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