using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>Builds an ElementPoolService with Window/Button/TextWindow/TextBox factories registered -- replaces what ElementPoolService's own constructor used to do, before that moved to ElementFactoryRegistry. Button is included here (not left for individual tests to register) since any test that creates a CanUserClose/CanUserMinimize window pulls one in via CloseBehavior/MinimizeRestoreBehavior.</summary>
internal static class TestElementPoolServiceFactory
{
    public static ElementPoolService Create(FontService fontService, GlyphRenderer glyphRenderer)
    {
        var pool = new ElementPoolService();
        pool.RegisterFactory<Window>(() => new Window(fontService, pool, glyphRenderer));
        pool.RegisterFactory<Button>(() => new Button(fontService, pool, glyphRenderer));
        pool.RegisterFactory<TextWindow>(() => new TextWindow(fontService, pool, glyphRenderer));
        pool.RegisterFactory<TextBox>(() => new TextBox(fontService, pool, glyphRenderer));
        return pool;
    }
}
