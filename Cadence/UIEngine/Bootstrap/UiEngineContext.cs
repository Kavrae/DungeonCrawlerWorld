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
    SpriteEffectService spriteEffectService,
    SpriteEffectBatch spriteEffectBatch,
    UiRenderService renderService,
    SpriteSheetService spriteSheetService,
    ElementPoolService elementPoolService)
{
    public FontService FontService { get; } = fontService;
    public SpriteBatchRenderer SpriteBatchRenderer { get; } = spriteBatchRenderer;
    public SpriteEffectService SpriteEffectService { get; } = spriteEffectService;

    /// <summary>What everything draws with (UiRenderService); the one every element takes.</summary>
    public UiRenderService RenderService { get; } = renderService;

    public SpriteSheetService SpriteSheetService { get; } = spriteSheetService;
    public ElementPoolService ElementPoolService { get; } = elementPoolService;

    /// <summary>
    /// Fills RenderService with the device and what draws with it -- called once, from the host's LoadContent, after
    /// GraphicsDevice/unitRectangle both exist. Sources the batches from this context's own SpriteBatchRenderer and sprite
    /// effect batch, so a host crosses into the framework once, through this one call.
    /// </summary>
    public void LoadContent(GraphicsDevice graphicsDevice, Texture2D unitRectangle) =>
        RenderService.Initialize(graphicsDevice, SpriteBatchRenderer.GetSpriteBatch(), spriteEffectBatch, unitRectangle);

    /// <summary>Registers the element factories of every control UIEngine itself defines, so no host repeats the list.</summary>
    /// <remarks>
    /// Called once by a host after it builds its layers, since TextBox opens contextMenuController's menu (and DropdownField its dropdown) and shows
    /// cursorTextContent's "Copied" (none when the host has no cursor text). A host registers only its own element types after this.
    /// </remarks>
    public void RegisterFrameworkElementFactories(ContextMenuController contextMenuController, CursorTextContent? cursorTextContent = null)
    {
        var pool = ElementPoolService;
        var fonts = FontService;
        var sprites = SpriteSheetService;
        var render = RenderService;

        pool.RegisterFactory(() => new Window(fonts, pool, render));
        pool.RegisterFactory(() => new TextWindow(fonts, pool, render));
        pool.RegisterFactory(() => new Button(fonts, pool, render, sprites));
        pool.RegisterFactory(() => new TextBox(fonts, pool, render, cursorTextContent, contextMenuController));
        pool.RegisterFactory(() => new NumericField(fonts, pool, render, cursorTextContent, contextMenuController));
        pool.RegisterFactory(() => new ColorField(fonts, pool, render, cursorTextContent, contextMenuController));
        pool.RegisterFactory(() => new Dropdown(fonts, pool, render));
        pool.RegisterFactory(() => new DropdownField(fonts, pool, render, contextMenuController.Dropdowns));
        pool.RegisterFactory(() => new ListView(fonts, pool, render));
        pool.RegisterFactory(() => new ListEditor(fonts, pool, render));
        pool.RegisterFactory(() => new SplitPane(fonts, pool, render));
        pool.RegisterFactory(() => new ModalDialog(fonts, pool, render));
        pool.RegisterFactory(() => new Tooltip(fonts, pool, render));
        pool.RegisterFactory(() => new Folder(fonts, pool, render, sprites));
        pool.RegisterFactory(() => new GridControl(fonts, pool, render));
        pool.RegisterFactory(() => new Toggle(fonts, pool, render));
        pool.RegisterFactory(() => new TextDivider(fonts, pool, render));
        pool.RegisterFactory(() => new SeparatorBar(fonts, pool, render));
        pool.RegisterFactory(() => new FractionBarElement(fonts, pool, render));
    }
}