namespace Presentation.UI.Content;

/// <summary>An element that shows item stacks -- an inventory cell, a hotbar slot bound to an item -- so everything showing a stack a context menu is open for can highlight with it (ContextMenuItemStacks).</summary>
public interface IShowsItemStacks
{
    /// <summary>Whether this shows entityId's stack stackInstanceId.</summary>
    bool ShowsStack(int entityId, uint stackInstanceId);
}
