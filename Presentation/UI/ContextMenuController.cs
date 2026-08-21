using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// Opens/closes the single shared ContextMenu popup -- the "shared mechanics" half of this
/// codebase's context-menu split (see ContextMenuOption's own doc comment for the "distributed
/// content" half: what options actually appear is entirely up to whoever detects the right-click,
/// e.g. MapWindow for a corpse's "Loot", TextBox for Cut/Copy/Paste/Select All). Positioned via
/// the same PopupPositioning math Tooltip already uses, anchored SouthEast of a zero-size
/// Rectangle at the cursor -- the menu's top-left corner pins at the cursor, growing down and to
/// the right, the native OS convention for a context menu.
/// </summary>
public sealed class ContextMenuController(ElementPoolService elementPoolService)
{
    /// <summary>
    /// Generous ceiling for the menu's own Fixed-mode MaximumSize -- comfortably larger than any
    /// realistic option list could ever need (the same "generous ceiling" reasoning
    /// NotificationCenter.FolderMaximumSize already uses for its own WrapContent Folder). Fixed
    /// mode's own size clamp (RecalculateFixedSize) reads MaximumSize as whatever Build last set
    /// it to and never re-derives it later -- a parent-null Element (see ContextMenu's own doc
    /// comment on why it must stay top-level) has no parent ContentSize to fall back on either,
    /// so without an explicit MaximumSize here it defaults to (0,0) and every later Show/SetBounds
    /// call gets silently clamped down to nothing, however large a size it actually asks for.
    /// </summary>
    private static readonly Vector2 MaximumMenuSize = new(600, 2000);

    private ContextMenu _menu = null!;

    public bool IsOpen => _menu.IsVisible;

    /// <summary>The live popup itself -- UiInputController reads its Rectangle directly to tell an outside click from one that lands on the menu.</summary>
    internal Element Menu => _menu;

    public void Initialize(UiLayerStack layers)
    {
        _menu = elementPoolService.CreateElement<ContextMenu>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { IsVisible = false, MaximumSize = MaximumMenuSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false, CanUserFocus = false },
        });
        _menu.Initialize();
        layers.Add(UiLayer.ContextMenu, _menu);
    }

    public void Open(Vector2 cursorPosition, IReadOnlyList<ContextMenuOption> options)
    {
        var cursorRectangle = new Rectangle((int)cursorPosition.X, (int)cursorPosition.Y, 0, 0);
        var topLeft = PopupPositioning.GetPosition(cursorRectangle, Vector2.Zero, PopupAnchor.SouthEast, Vector2.Zero);
        _menu.Show(topLeft, options);
    }

    public void Close() => _menu.Hide();
}
