using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>Builds an ElementPoolService with Window/Button/Dropdown/TextWindow/TextBox factories registered -- replaces what ElementPoolService's own constructor used to do, before that moved to ElementFactoryRegistry. Button is included here (not left for individual tests to register) since any test that creates a CanUserClose/CanUserMinimize window pulls one in via CloseBehavior/MinimizeRestoreBehavior; Dropdown likewise, since any test that constructs a DropdownController pulls one in via its own Initialize.</summary>
internal static class TestElementPoolServiceFactory
{
    /// <summary>Stand-in screen size for anything that would otherwise need a real GraphicsDevice.Viewport.Bounds -- unavailable headlessly. Matches PlayerHealthBarContentTests' own ScreenBounds constant.</summary>
    private static readonly Rectangle ScreenBounds = new(0, 0, 1920, 1080);

    /// <param name="createTextWindow">A TextWindow factory in place of the plain one, for a test that captures what it creates.</param>
    /// <param name="createTextBox">A TextBox factory in place of the plain one, for a test whose text boxes open a context menu.</param>
    public static ElementPoolService Create(FontService fontService, LabelRenderer labelRenderer, Func<ElementPoolService, TextWindow>? createTextWindow = null, Func<ElementPoolService, TextBox>? createTextBox = null)
    {
        var pool = new ElementPoolService();
        pool.RegisterFactory<Window>(() => new Window(fontService, pool, labelRenderer));
        pool.RegisterFactory<Button>(() => new Button(fontService, pool, labelRenderer));
        pool.RegisterFactory<Dropdown>(() => new Dropdown(fontService, pool, labelRenderer));
        pool.RegisterFactory<TextWindow>(() => createTextWindow?.Invoke(pool) ?? new TextWindow(fontService, pool, labelRenderer));
        pool.RegisterFactory<TextBox>(() => createTextBox?.Invoke(pool) ?? new TextBox(fontService, pool, labelRenderer));
        return pool;
    }

    /// <summary>Constructs and initializes a ContextMenuController over a DropdownController with ScreenBoundsOverrideForTests already set, since ElementPoolService.GraphicsDevice is never wired up headlessly -- see DropdownController's own doc comment on that property. Takes the caller's own UiLayerStack (rather than always minting a fresh one) so a test can still share it with other elements that need the same layer stack, e.g. a TextBox opening this same controller's menu.</summary>
    public static ContextMenuController CreateContextMenuController(ElementPoolService elementPoolService, UiLayerStack layers)
    {
        return new ContextMenuController(CreateDropdownController(elementPoolService, layers));
    }

    /// <summary>A DropdownController initialized into layers, clamped to a stand-in screen.</summary>
    public static DropdownController CreateDropdownController(ElementPoolService elementPoolService, UiLayerStack layers)
    {
        var dropdownController = new DropdownController(elementPoolService) { ScreenBoundsOverrideForTests = ScreenBounds };
        dropdownController.Initialize(layers);
        return dropdownController;
    }
}
