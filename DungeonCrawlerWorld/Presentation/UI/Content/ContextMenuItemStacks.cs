using UIEngine.UI;

namespace Presentation.UI.Content;

/// <summary>Which item stacks the open context menu is about: those its target shows (IShowsItemStacks).</summary>
public static class ContextMenuItemStacks
{
    /// <summary>Whether the open context menu was opened for something showing entityId's stack stackInstanceId.</summary>
    public static bool IsAbout(ContextMenuController contextMenus, int entityId, uint stackInstanceId) =>
        contextMenus.Target is IShowsItemStacks target && target.ShowsStack(entityId, stackInstanceId);
}
