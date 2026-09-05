namespace Presentation.UI;

/// <summary>
/// Owns every root Element the shell draws/updates/hit-tests, grouped by UiLayer -- replaces
/// what used to be four independently-hardcoded List&lt;Element&gt; fields (Base/StaticHud/
/// DynamicHud/User) threaded individually through ShellBootstrapper/UiInputController/
/// NotificationCenter/InventoryWindowController/HotbarController with one structure keyed by an
/// enum whose declaration order defines z-order. A window is now added by naming its UiLayer
/// (layers.Add(UiLayer.DynamicHud, window)) rather than a caller being handed a reference to
/// whichever specific list happens to represent that tier.
/// </summary>
public sealed class UiLayerStack
{
    private readonly Dictionary<UiLayer, List<Element>> _byLayer;

    /// <summary>
    /// Currently-open menu windows -- membership only, not z-order (see TopmostMenuWindow/
    /// BottommostMenuWindow, which read actual draw/raise order out of _byLayer instead). Menu
    /// mode -- named for the WoW/Diablo/PoE bag-open convention this mimics, not a "modal dialog"
    /// in the strict web/desktop sense (see UiInputController.TryHitTestInteraction's own doc
    /// comment) -- is an "interaction mode switch," not a single locked-to-one-window state: while
    /// any menu window is open, input reaches every open one (e.g. Inventory and Ability Scores
    /// can both be open and both individually clickable) plus every element MarkMenuModeExempt
    /// opted in (see that method). What's exclusive is menu-vs-everything-else, not
    /// menu-window-vs-menu-window.
    /// </summary>
    private readonly List<Element> _menuWindows = [];

    /// <summary>
    /// Elements that stay reachable -- and, per ShellContext.Draw, undimmed -- even while menu
    /// mode blocks everything else, without themselves being a menu window (see OpenMenuWindow).
    /// The hotbar and the Notification/Inventory folder tiles are the concrete cases: assigning an
    /// item to the hotbar while Inventory is open, or opening a second menu window from its
    /// folder while one is already open, are both normal parts of the workflow menu mode exists
    /// to support, not something it should block. Expected to stay a small, deliberately-curated
    /// set -- see MarkMenuModeExempt's own doc comment.
    /// </summary>
    private readonly HashSet<Element> _menuModeExemptElements = [];

    /// <summary>Every UiLayer starts as its own new, empty list.</summary>
    public UiLayerStack()
    {
        _byLayer = new Dictionary<UiLayer, List<Element>>();
        foreach (var layer in Enum.GetValues<UiLayer>())
        {
            _byLayer[layer] = [];
        }
    }

    /// <summary>
    /// Marks window as an open menu window -- see UiInputController.TryHitTestInteraction/
    /// HandleEscape for what that grants it, and MenuModeDimRenderer for the visual half.
    /// Idempotent: a no-op if window is already a menu window, since Add below can promote a
    /// window into this set on the same call a caller might also explicitly promote it on (e.g. a
    /// System notification opened while menu mode is already active) -- without idempotency,
    /// that double-add would leave a stale second entry behind after just one CloseMenuWindow
    /// call, since List.Remove only removes the first match.
    /// </summary>
    public void OpenMenuWindow(Element window)
    {
        if (!IsMenuWindow(window))
        {
            _menuWindows.Add(window);
        }
    }

    /// <summary>Removes window from the open menu windows. Safe/no-op if window was never opened as one.</summary>
    public bool CloseMenuWindow(Element window) => _menuWindows.Remove(window);

    /// <summary>True if window is a currently-open menu window.</summary>
    public bool IsMenuWindow(Element window) => _menuWindows.Contains(window);

    /// <summary>
    /// Marks element as menu-mode-exempt -- see _menuModeExemptElements' own doc comment for what
    /// that grants it. A one-time opt-in call (e.g. HotbarController.Initialize, once, right after
    /// adding its window to StaticHud), not a per-frame check -- deliberately not automatic for
    /// every StaticHud/DynamicHud element, since the expectation (see this class's own field
    /// comment) is that most of the UI will keep growing while only a small, deliberately-chosen
    /// slice of it should ever bypass menu mode's blocking.
    /// </summary>
    public void MarkMenuModeExempt(Element element) => _menuModeExemptElements.Add(element);

    /// <summary>True if element was opted out of menu mode's blocking via MarkMenuModeExempt.</summary>
    public bool IsMenuModeExempt(Element element) => _menuModeExemptElements.Contains(element);

    /// <summary>
    /// The open menu window currently drawn frontmost (last raised-to-front among open menu
    /// windows -- see Element.RaiseToFront/UiLayerStack.RaiseToFront -- independent of which one
    /// opened first or last). What Escape dismisses (see
    /// UiInputController.CloseTopmostClosableWindow), and where TryHitTestInteraction's menu-window
    /// hit-testing starts. Null if no menu window is open.
    /// </summary>
    public Element? TopmostMenuWindow => FindMenuWindow(LayersDescending(), reverse: true);

    /// <summary>The open menu window currently drawn backmost among open menu windows -- where MenuModeDimRenderer draws (see ShellContext.Draw), so every open menu window, not just the frontmost, renders above it. Null if no menu window is open.</summary>
    public Element? BottommostMenuWindow => FindMenuWindow(LayersAscending(), reverse: false);

    private Element? FindMenuWindow(IEnumerable<UiLayer> layers, bool reverse)
    {
        foreach (var layer in layers)
        {
            var elements = _byLayer[layer];
            if (reverse)
            {
                for (var index = elements.Count - 1; index >= 0; index--)
                {
                    if (IsMenuWindow(elements[index]))
                    {
                        return elements[index];
                    }
                }
            }
            else
            {
                foreach (var element in elements)
                {
                    if (IsMenuWindow(element))
                    {
                        return element;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>True while any menu window is open -- feeds GameLoop's simulation-pause check alongside MapWindow.IsPaused.</summary>
    public bool IsMenuModeActive => _menuWindows.Count > 0;

    public IReadOnlyList<Element> this[UiLayer layer] => _byLayer[layer];

    /// <summary>
    /// Adds element to layer -- and, if menu mode is already active, promotes it straight into
    /// the open menu-window set too (see OpenMenuWindow). Anything newly added while menu mode is
    /// active can only have been spawned by a menu window or a menu-mode-exempt element (nothing
    /// else is reachable to spawn it) -- e.g. opening a second notification from the (exempt)
    /// Notification folder while a System notification already has menu mode active -- so it's
    /// part of that same interaction, not something menu mode should then turn around and block/
    /// dim. Windows added before menu mode is ever active (the whole startup-time HUD build, plus
    /// each folder's own persistent tile/icon) are unaffected -- IsMenuModeActive is false then.
    /// </summary>
    public void Add(UiLayer layer, Element element)
    {
        _byLayer[layer].Add(element);

        if (IsMenuModeActive)
        {
            OpenMenuWindow(element);
        }
    }

    public bool Remove(UiLayer layer, Element element) => _byLayer[layer].Remove(element);

    public bool Contains(UiLayer layer, Element element) => _byLayer[layer].Contains(element);

    /// <summary>Which layer element currently lives in, if any -- for code that needs to ask an element's own tier rather than being told it explicitly (e.g. focus redirect deciding whether a closing root element's siblings are the DynamicHud group).</summary>
    public UiLayer? LayerOf(Element element)
    {
        foreach (var (layer, elements) in _byLayer)
        {
            if (elements.Contains(element))
            {
                return layer;
            }
        }

        return null;
    }

    /// <summary>Moves element to the end of its own layer's list -- draws last (on top), wins future hit-tests against same-layer siblings. Root-element counterpart to Element.RaiseToFront, which only handles the parented case (a root has no ParentElement of its own to raise within). No-op if element isn't in any layer.</summary>
    public void RaiseToFront(Element element)
    {
        if (LayerOf(element) is not { } layer)
        {
            return;
        }

        var elements = _byLayer[layer];
        elements.Remove(element);
        elements.Add(element);
    }

    /// <summary>Every declared UiLayer, bottom to top -- Draw/Update/LoadContent order.</summary>
    public static IEnumerable<UiLayer> LayersAscending() => Enum.GetValues<UiLayer>();

    /// <summary>Every declared UiLayer, top to bottom -- hit-test order (a higher tier always wins over a lower one, regardless of screen position, see UiInputController's own doc comment).</summary>
    public static IEnumerable<UiLayer> LayersDescending() => Enum.GetValues<UiLayer>().Reverse();
}
