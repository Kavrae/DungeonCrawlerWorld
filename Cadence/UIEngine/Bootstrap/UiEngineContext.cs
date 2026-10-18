using Microsoft.Xna.Framework.Graphics;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.Content;

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

    /// <summary>Registers the element factories of every control UIEngine itself defines, so no host repeats the list.</summary>
    /// <remarks>
    /// Called once by a host after it builds its layers, since TextBox opens contextMenuController's menu (and DropdownField its dropdown) and shows
    /// cursorTextContent's "Copied" (none when the host has no cursor text). A host registers only its own element types after this.
    /// </remarks>
    public void RegisterFrameworkElementFactories(ContextMenuController contextMenuController, CursorTextContent? cursorTextContent = null)
    {
        var pool = ElementPoolService;
        var fonts = FontService;
        var labels = LabelRenderer;
        var sprites = SpriteSheetService;

        pool.RegisterFactory(() => new Window(fonts, pool, labels));
        pool.RegisterFactory(() => new TextWindow(fonts, pool, labels));
        pool.RegisterFactory(() => new Button(fonts, pool, labels, sprites));
        pool.RegisterFactory(() => new TextBox(fonts, pool, labels, cursorTextContent, contextMenuController));
        pool.RegisterFactory(() => new NumericField(fonts, pool, labels, cursorTextContent, contextMenuController));
        pool.RegisterFactory(() => new ColorField(fonts, pool, labels, cursorTextContent, contextMenuController));
        pool.RegisterFactory(() => new Dropdown(fonts, pool, labels));
        pool.RegisterFactory(() => new DropdownField(fonts, pool, labels, contextMenuController.Dropdowns));
        pool.RegisterFactory(() => new ListView(fonts, pool, labels));
        pool.RegisterFactory(() => new ListEditor(fonts, pool, labels));
        pool.RegisterFactory(() => new SplitPane(fonts, pool, labels));
        pool.RegisterFactory(() => new ModalDialog(fonts, pool, labels));
        pool.RegisterFactory(() => new Tooltip(fonts, pool, labels));
        pool.RegisterFactory(() => new Folder(fonts, pool, labels, sprites));
        pool.RegisterFactory(() => new GridControl(fonts, pool, labels));
        pool.RegisterFactory(() => new Toggle(fonts, pool, labels));
        pool.RegisterFactory(() => new TextDivider(fonts, pool, labels));
        pool.RegisterFactory(() => new SeparatorBar(fonts, pool, labels));
        pool.RegisterFactory(() => new FractionBarElement(fonts, pool, labels));
    }
}