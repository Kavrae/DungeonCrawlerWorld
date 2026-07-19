namespace Presentation.UI;

/// <summary>
/// A composable window-chrome capability (close, minimize, restore, and eventually
/// move/resize/dock) that attaches itself to a Window rather than being implemented as
/// virtual methods on Window itself. A window that doesn't attach a given behavior pays
/// no cost for it -- no fields, no branches, no button.
/// </summary>
public interface IWindowChromeBehavior
{
    void Attach(Window window);
}
