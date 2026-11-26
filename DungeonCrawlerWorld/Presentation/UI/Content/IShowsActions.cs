using UIEngine.UI;

namespace Presentation.UI.Content;

/// <summary>An element that shows one of the player's actions or a menu command -- an Actions window cell, a hotbar slot bound to one -- so everything showing what a context menu is open for can highlight with it (ContextMenuActions).</summary>
public interface IShowsActions
{
    /// <summary>Whether this shows the player's action actionId.</summary>
    bool ShowsAction(Guid actionId);

    /// <summary>Whether this shows menu command menuCommandId.</summary>
    bool ShowsMenuCommand(Guid menuCommandId);
}

/// <summary>Which action or menu command the open context menu is about: what its target shows (IShowsActions).</summary>
public static class ContextMenuActions
{
    /// <summary>Whether the open context menu was opened for something showing the player's action actionId.</summary>
    public static bool IsAboutAction(ContextMenuController contextMenus, Guid actionId) =>
        contextMenus.Target is IShowsActions target && target.ShowsAction(actionId);

    /// <summary>Whether the open context menu was opened for something showing menu command menuCommandId.</summary>
    public static bool IsAboutMenuCommand(ContextMenuController contextMenus, Guid menuCommandId) =>
        contextMenus.Target is IShowsActions target && target.ShowsMenuCommand(menuCommandId);
}
