using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>Builds an ElementPoolService with Window/Button/ContextMenu/TextWindow/TextBox factories registered -- replaces what ElementPoolService's own constructor used to do, before that moved to ElementFactoryRegistry. Button is included here (not left for individual tests to register) since any test that creates a CanUserClose/CanUserMinimize window pulls one in via CloseBehavior/MinimizeRestoreBehavior; ContextMenu likewise, since any test that constructs a ContextMenuController pulls one in via its own Initialize.</summary>
internal static class TestElementPoolServiceFactory
{
    public static ElementPoolService Create(FontService fontService, LabelRenderer labelRenderer)
    {
        var pool = new ElementPoolService();
        pool.RegisterFactory<Window>(() => new Window(fontService, pool, labelRenderer));
        pool.RegisterFactory<Button>(() => new Button(fontService, pool, labelRenderer));
        pool.RegisterFactory<ContextMenu>(() => new ContextMenu(fontService, pool, labelRenderer));
        pool.RegisterFactory<TextWindow>(() => new TextWindow(fontService, pool, labelRenderer));
        pool.RegisterFactory<TextBox>(() => new TextBox(fontService, pool, labelRenderer));
        return pool;
    }
}
