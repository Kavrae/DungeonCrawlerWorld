using Microsoft.Xna.Framework.Graphics;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace UIEngine.Bootstrap;

/// <summary>Bundles the window framework's services, produced by UiEngineBootstrapper.</summary>
public sealed class UiEngineContext(
    FontService fontService,
    SpriteBatchRenderer spriteBatchRenderer,
    LabelRenderer labelRenderer,
    SpriteSheetService spriteSheetService,
    ElementPoolService elementPoolService)
{
    public FontService FontService { get; } = fontService;
    public SpriteBatchRenderer SpriteBatchRenderer { get; } = spriteBatchRenderer;
    public LabelRenderer LabelRenderer { get; } = labelRenderer;
    public SpriteSheetService SpriteSheetService { get; } = spriteSheetService;
    public ElementPoolService ElementPoolService { get; } = elementPoolService;

    /// <summary>
    /// Captures the render services ElementPoolService needs for every Element's Draw/DrawContent/
    /// DrawHeader (see ElementPoolService.Initialize's own doc comment) -- called once, from
    /// the host's LoadContent, after GraphicsDevice/unitRectangle both exist. Sources spriteBatch
    /// from this context's own SpriteBatchRenderer rather than taking it as a parameter too, since
    /// UiEngineContext already owns that reference -- a host only needs to cross into
    /// the framework once, through this one call, not reach into ElementPoolService directly.
    /// </summary>
    public void LoadContent(GraphicsDevice graphicsDevice, Texture2D unitRectangle) =>
        ElementPoolService.Initialize(graphicsDevice, SpriteBatchRenderer.GetSpriteBatch(), unitRectangle);
}