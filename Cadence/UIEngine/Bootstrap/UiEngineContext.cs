using Microsoft.Xna.Framework.Graphics;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.Content;

namespace UIEngine.Bootstrap;

/// <summary>Bundles the window framework's services, produced by UiEngineBootstrapper.</summary>
public sealed class UiEngineContext(
    FontService fontService,
    SpriteEffectService spriteEffectService,
    SpriteEffectBatch spriteEffectBatch,
    UiRenderService renderService,
    SpriteSheetService spriteSheetService,
    ElementPoolService elementPoolService)
{
    public FontService FontService { get; } = fontService;
    public SpriteEffectService SpriteEffectService { get; } = spriteEffectService;

    /// <summary>What everything draws with (UiRenderService); the one every element takes.</summary>
    public UiRenderService RenderService { get; } = renderService;

    public SpriteSheetService SpriteSheetService { get; } = spriteSheetService;
    public ElementPoolService ElementPoolService { get; } = elementPoolService;

    /// <summary>The services every element is built with; what each factory hands its element.</summary>
    public ElementServices ElementServices { get; } = new(fontService, elementPoolService, renderService);

    /// <summary>
    /// Fills RenderService with the device and what draws with it -- called once, from the host's LoadContent, after
    /// GraphicsDevice/unitRectangle both exist. Hands it this context's sprite effect batch, so a host crosses into the
    /// framework once, through this one call.
    /// </summary>
    public void LoadContent(GraphicsDevice graphicsDevice, Texture2D unitRectangle) =>
        RenderService.Initialize(graphicsDevice, spriteEffectBatch, unitRectangle);

    /// <summary>Registers the element factories of every control UIEngine itself defines, so no host repeats the list.</summary>
    /// <remarks>
    /// Called once by a host after it builds its layers, since TextBox opens contextMenuController's menu (and DropdownField its dropdown) and shows
    /// cursorTextContent's "Copied" (none when the host has no cursor text). A host registers only its own element types after this.
    /// </remarks>
    public void RegisterFrameworkElementFactories(ContextMenuController contextMenuController, CursorTextContent? cursorTextContent = null)
    {
        var pool = ElementPoolService;
        var services = ElementServices;
        var sprites = SpriteSheetService;

        pool.RegisterFactory(() => new Window(services));
        pool.RegisterFactory(() => new TextWindow(services));
        pool.RegisterFactory(() => new Button(services, sprites));
        pool.RegisterFactory(() => new TextBox(services, cursorTextContent, contextMenuController));
        pool.RegisterFactory(() => new NumericField(services, cursorTextContent, contextMenuController));
        pool.RegisterFactory(() => new ColorField(services, cursorTextContent, contextMenuController));
        pool.RegisterFactory(() => new Dropdown(services));
        pool.RegisterFactory(() => new DropdownField(services, contextMenuController.Dropdowns));
        pool.RegisterFactory(() => new ListView(services));
        pool.RegisterFactory(() => new ListEditor(services));
        pool.RegisterFactory(() => new SplitPane(services));
        pool.RegisterFactory(() => new ModalDialog(services));
        pool.RegisterFactory(() => new Tooltip(services));
        pool.RegisterFactory(() => new TooltipRowsElement(services));
        pool.RegisterFactory(() => new Folder(services, sprites));
        pool.RegisterFactory(() => new GridControl(services));
        pool.RegisterFactory(() => new Toggle(services));
        pool.RegisterFactory(() => new ToggleList(services));
        pool.RegisterFactory(() => new TextDivider(services));
        pool.RegisterFactory(() => new SeparatorBar(services));
        pool.RegisterFactory(() => new FractionBarElement(services));
    }
}