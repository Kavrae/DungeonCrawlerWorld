namespace UIEngine.UI;

/// <summary>
/// The app's UI draw/update/hit-test tiers, bottom to top -- declaration order IS z-order, the
/// one order every pass reads (Draw/Update ascending, hit-test descending). Adding a tier is
/// exactly one new member in the right declaration position; every pass built on UiLayerStack
/// picks it up automatically.
///
/// Values are gap-spaced (1000 apart), not sequential -- a window's layer may be persisted
/// data, so a later patch inserting a new tier between two existing ones must not renumber a value an old save file already wrote out. Sequential values would
/// make every insertion a save-compat break; gap-spacing lets a new tier slot in anywhere (e.g.
/// 2500, between DynamicHud and Tooltip) without touching the others. Even so, prefer persisting
/// a window's layer by name (see UiLayerNameParser), not this raw int -- gap-spacing only
/// protects insertion, not a tier being renamed or removed outright, which needs an explicit
/// save-version migration regardless.
/// </summary>
public enum UiLayer
{
    /// <summary>World content and fixed, distinct panels.</summary>
    Base = 0,

    /// <summary>Persistent chrome -- bars, icon bars, status readouts, not generally opened/closed by the player.</summary>
    StaticHud = 1000,

    /// <summary>Notifications and openable windows -- popups the player actually opens/closes.</summary>
    DynamicHud = 2000,

    /// <summary>Hover-triggered informational popups (see Tooltip) -- always above DynamicHud (so a tooltip renders over the window it's describing) and below User (so an active drag's own feedback is never obscured by an unrelated tooltip).</summary>
    Tooltip = 3000,

    /// <summary>Cursor-following drag feedback and other transient user-driven visual effects.</summary>
    User = 4000,

    /// <summary>A single open right-click Dropdown (see ContextMenuController) -- always the true topmost tier, above even User: an open context menu is the player's active focus and must never be obscured, including by drag feedback. Also reachable during menu mode (see UiInputController.LayersAboveMenuMode) -- a search box inside an open menu window still needs its context menu.</summary>
    ContextMenu = 5000,
}
