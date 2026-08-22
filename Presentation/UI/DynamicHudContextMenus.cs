namespace Presentation.UI;

/// <summary>
/// Shared "Close" / "Close All" context-menu option builder for any DynamicHud-tier window --
/// Inventory, Ability Scores, a corpse/secondary inventory window, ... Deliberately a plain
/// static helper, not an interface (see the AdvancedMapContextMenu TODO's own explicit
/// rejection of an IContextMenuProvider abstraction) -- a caller just wires its own window's
/// OnRightClicked to open the list this builds.
/// </summary>
public static class DynamicHudContextMenus
{
    public static IReadOnlyList<ContextMenuOption> BuildCloseMenu(Window window, UiLayerStack layers) =>
    [
        new ContextMenuOption("Close", null, Enabled: true, window.Close),
        new ContextMenuOption("Close All", null, Enabled: true, () => CloseAll(layers)),
    ];

    /// <summary>
    /// Every DynamicHud element that's actually a Window (not e.g. the Inventory/Notification
    /// folder tiles, which are a plain Element and have no "closed" state to begin with) --
    /// snapshotted via .ToArray() first, since Window.Close() -> ElementPoolService.CloseElement
    /// removes the closed window from layers[UiLayer.DynamicHud] as part of closing (each
    /// window's own Closed handler calls layers.Remove), so iterating the live list while
    /// closing would skip whatever shifted into the just-removed index.
    /// </summary>
    private static void CloseAll(UiLayerStack layers)
    {
        foreach (var element in layers[UiLayer.DynamicHud].ToArray())
        {
            if (element is Window window)
            {
                window.Close();
            }
        }
    }
}
