using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Workshop.Debug;

/// <summary>What the Debug tab's view builds its windows with: the element pool and fonts, the layers its windows go in, how it registers an input handler for as long as it is open, and the status line.</summary>
/// <param name="ContextMenus">The shell's context menu, for a right-click on a row.</param>
public sealed record DebugTabServices(ElementPoolService Pool, FontService Fonts, UiRenderService Render, UiLayerStack Layers, Action<IUiInputHandler> AddInputHandler, IWorkshopStatus Status, ContextMenuController ContextMenus)
{
    /// <summary>Registers the Debug tab's own elements with the pool -- once per pool.</summary>
    public static void RegisterElementFactories(ElementServices services)
    {
        services.ElementPoolService.RegisterFactory(() => new DiagnosticsTimelineView(services));
        services.ElementPoolService.RegisterFactory(() => new DiagnosticsGaugeList(services));
    }
}
