using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Workshop.Debug;

/// <summary>What the Debug tab's view builds its windows with: the element pool and fonts, the layers its windows go in, how it registers an input handler for as long as it is open, the status line, and the clipboard.</summary>
/// <param name="CopyToClipboard">Puts text on the system clipboard.</param>
/// <param name="ContextMenus">The shell's context menu, for a right-click on a row.</param>
public sealed record DebugTabServices(ElementPoolService Pool, FontService Fonts, UiRenderService Render, UiLayerStack Layers, Action<IUiInputHandler> AddInputHandler, IWorkshopStatus Status, Action<string> CopyToClipboard, ContextMenuController ContextMenus)
{
    /// <summary>Registers the Debug tab's own elements with the pool -- once per pool.</summary>
    public static void RegisterElementFactories(ElementPoolService pool, FontService fonts, UiRenderService render)
    {
        pool.RegisterFactory(() => new DiagnosticsTimelineView(fonts, pool, render));
        pool.RegisterFactory(() => new DiagnosticsGaugeList(fonts, pool, render));
    }
}
