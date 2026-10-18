using Microsoft.Xna.Framework;

namespace UIEngine.UI;

/// <summary>
/// Shared "Close" / "Close All" context-menu option builder for any DynamicHud-tier window. Deliberately a plain
/// static helper, not an interface -- a caller just wires its own window's
/// OnRightClicked to open the list this builds, or calls WireCloseContextMenu below to do that
/// same one-line wiring itself.
/// </summary>
public static class DynamicHudContextMenus
{
    public static IReadOnlyList<DropdownOption> BuildCloseMenu(Window window, UiLayerStack layers) =>
    [
        new DropdownOption("Close", null, Enabled: true, window.Close),
        new DropdownOption("Close All", null, Enabled: true, () => CloseAll(layers)),
    ];

    /// <summary>Makes right-clicking window open its own Close/Close All menu.</summary>
    public static void WireCloseContextMenu(Window window, ContextMenuController contextMenuController, UiLayerStack layers) =>
        window.OnRightClicked = position => contextMenuController.Open(new Vector2(position.X, position.Y), BuildCloseMenu(window, layers));

    /// <summary>
    /// Every DynamicHud element that's actually a Window (not a folder tile, which is a plain Element and have no "closed" state to begin with) --
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
