namespace UIEngine.UI;

/// <summary>
/// Owns every root Element the shell draws/updates/hit-tests, grouped by UiLayer, whose
/// declaration order is z-order. A window is added by naming its layer
/// (layers.Add(UiLayer.DynamicHud, window)).
/// </summary>
public sealed class UiLayerStack
{
    /// <summary>Each layer's root elements in draw order, indexed by the layer's position in AscendingLayers.</summary>
    private readonly List<Element>[] _byLayer;

    /// <summary>The layer each root element is in, kept by Add and Remove, so LayerOf never scans the lists.</summary>
    private readonly Dictionary<Element, UiLayer> _layerByElement = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Currently-open menu windows -- membership only, not z-order (see TopmostMenuWindow/
    /// BottommostMenuWindow, which read actual draw/raise order out of the layer lists instead). Menu
    /// mode -- named for the WoW/Diablo/PoE bag-open convention this mimics, not a "modal dialog"
    /// in the strict web/desktop sense (see UiInputController.TryHitTestInteraction's own doc
    /// comment) -- is an "interaction mode switch," not a single locked-to-one-window state: while
    /// any menu window is open, input reaches every open one (two open
    /// windows are both clickable) plus every element MarkMenuModeExempt
    /// opted in (see that method). What's exclusive is menu-vs-everything-else, not
    /// menu-window-vs-menu-window.
    /// </summary>
    private readonly List<Element> _menuWindows = [];

    /// <summary>
    /// Elements that stay reachable -- and, per UiLayerHost.Draw, undimmed -- even while menu
    /// mode blocks everything else, without themselves being a menu window (see OpenMenuWindow).
    /// An icon bar items are dragged onto, or the folder tiles that open menu windows, are the kind:
    /// using them while a menu window is open is part of the workflow menu mode exists to support. Expected to stay a small, deliberately-curated
    /// set -- see MarkMenuModeExempt's own doc comment.
    /// </summary>
    private readonly HashSet<Element> _menuModeExemptElements = [];

    private static readonly UiLayer[] AscendingLayers = Enum.GetValues<UiLayer>();

    private static readonly UiLayer[] DescendingLayers = [.. Enumerable.Reverse(AscendingLayers)];

    /// <summary>Changes whenever a root element is added, removed or raised, or a menu window opens or closes -- for a reader that keeps what it found in the layers (UiInputController's hovered element).</summary>
    public int Version { get; private set; }

    /// <summary>Every UiLayer starts as its own new, empty list.</summary>
    public UiLayerStack()
    {
        _byLayer = new List<Element>[AscendingLayers.Length];
        for (var layerIndex = 0; layerIndex < _byLayer.Length; layerIndex++)
        {
            _byLayer[layerIndex] = [];
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
            Version++;
        }
    }

    /// <summary>Removes window from the open menu windows. Safe/no-op if window was never opened as one.</summary>
    public bool CloseMenuWindow(Element window)
    {
        if (!_menuWindows.Remove(window))
        {
            return false;
        }

        Version++;
        return true;
    }

    /// <summary>True if window is a currently-open menu window.</summary>
    public bool IsMenuWindow(Element window) => _menuWindows.Contains(window);

    /// <summary>
    /// Marks element as menu-mode-exempt -- see _menuModeExemptElements' own doc comment for what
    /// that grants it. A one-time opt-in call, right after adding
    /// the element to its layer, not a per-frame check -- deliberately not automatic for
    /// every StaticHud/DynamicHud element, since the expectation (see this class's own field
    /// comment) is that most of the UI will keep growing while only a small, deliberately-chosen
    /// slice of it should ever bypass menu mode's blocking.
    /// </summary>
    public void MarkMenuModeExempt(Element element) => _menuModeExemptElements.Add(element);

    /// <summary>Withdraws element's menu-mode exemption -- for an exempt window that closes, so the pooled Element isn't still exempt when it's reused as a different window.</summary>
    public void UnmarkMenuModeExempt(Element element) => _menuModeExemptElements.Remove(element);

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

    /// <summary>The open menu window currently drawn backmost among open menu windows -- where MenuModeDimRenderer draws (see UiLayerHost.Draw), so every open menu window, not just the frontmost, renders above it. Null if no menu window is open.</summary>
    public Element? BottommostMenuWindow => FindMenuWindow(LayersAscending(), reverse: false);

    private Element? FindMenuWindow(ReadOnlySpan<UiLayer> layers, bool reverse)
    {
        foreach (var layer in layers)
        {
            var elements = ElementsIn(layer);
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

    /// <summary>True while any menu window is open -- a host may pause its simulation on it.</summary>
    public bool IsMenuModeActive => _menuWindows.Count > 0;

    public IReadOnlyList<Element> this[UiLayer layer] => ElementsIn(layer);

    /// <summary>
    /// Adds element to layer -- and, if menu mode is already active, promotes it straight into
    /// the open menu-window set too (see OpenMenuWindow). Anything newly added while menu mode is
    /// active can only have been spawned by a menu window or a menu-mode-exempt element (nothing
    /// else is reachable to spawn it) -- e.g. opening a second notification from the (exempt)
    /// Notification folder while a System notification already has menu mode active -- so it's
    /// part of that same interaction, not something menu mode should then turn around and block/
    /// dim. Windows added before menu mode is ever active (the whole startup-time HUD build, plus
    /// each folder's own persistent tile/icon) are unaffected -- IsMenuModeActive is false then.
    /// An element already marked menu-mode-exempt is never promoted: exempt means reachable during
    /// menu mode without being a menu window, so it can't hold menu mode (and the simulation's
    /// pause) open after the menu windows it opened beside have closed.
    /// </summary>
    public void Add(UiLayer layer, Element element)
    {
        ElementsIn(layer).Add(element);
        _layerByElement[element] = layer;
        Version++;

        if (IsMenuModeActive && !IsMenuModeExempt(element))
        {
            OpenMenuWindow(element);
        }
    }

    public bool Remove(UiLayer layer, Element element)
    {
        if (!ElementsIn(layer).Remove(element))
        {
            return false;
        }

        if (_layerByElement.TryGetValue(element, out var recordedLayer) && recordedLayer == layer && !ElementsIn(layer).Contains(element))
        {
            _layerByElement.Remove(element);
        }

        Version++;
        return true;
    }

    public bool Contains(UiLayer layer, Element element) => ElementsIn(layer).Contains(element);

    /// <summary>Which layer element currently lives in, if any -- for code that needs to ask an element's own tier rather than being told it explicitly (e.g. focus redirect deciding whether a closing root element's siblings are the DynamicHud group).</summary>
    public UiLayer? LayerOf(Element element) => _layerByElement.TryGetValue(element, out var layer) ? layer : null;

    private List<Element> ElementsIn(UiLayer layer) => _byLayer[IndexOf(layer)];

    private static int IndexOf(UiLayer layer)
    {
        for (var layerIndex = 0; layerIndex < AscendingLayers.Length; layerIndex++)
        {
            if (AscendingLayers[layerIndex] == layer)
            {
                return layerIndex;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(layer), layer, "Not a declared UiLayer.");
    }

    /// <summary>Moves element to the end of its own layer's list -- draws last (on top), wins future hit-tests against same-layer siblings. Root-element counterpart to Element.RaiseToFront, which only handles the parented case (a root has no ParentElement of its own to raise within). No-op if element isn't in any layer.</summary>
    public void RaiseToFront(Element element)
    {
        if (LayerOf(element) is not { } layer)
        {
            return;
        }

        var elements = ElementsIn(layer);
        elements.Remove(element);
        elements.Add(element);
        Version++;
    }

    /// <summary>
    /// Closes every currently-open Window with CanUserClose true, across every layer --
    /// unconditional: no menu-mode short-circuit (a held Escape or an item activation both clear a
    /// modal menu window too, not just whatever's behind it) and no per-layer eligibility filter
    /// (contrast UiInputController.CloseTopmostClosableWindow's single-tap Escape behavior, which
    /// keeps both of those for a narrower, one-window-at-a-time dismiss). The one shared
    /// implementation for both "the player held Escape" (UiInputController.HandleEscape) and any host
    /// action that needs every closable window out of the way. Snapshots each layer's list first --
    /// Window.Close() self-removes via its own Closed handler, which would corrupt an in-progress
    /// enumeration otherwise.
    /// </summary>
    public void CloseAllClosableWindows()
    {
        foreach (var layer in LayersDescending())
        {
            var snapshot = ElementsIn(layer).ToArray();
            for (var index = snapshot.Length - 1; index >= 0; index--)
            {
                if (snapshot[index] is Window { CanUserClose: true } window)
                {
                    window.Close();
                }
            }
        }
    }

    /// <summary>Every declared UiLayer, bottom to top -- Draw/Update/LoadContent order.</summary>
    public static ReadOnlySpan<UiLayer> LayersAscending() => AscendingLayers;

    /// <summary>Every declared UiLayer, top to bottom -- hit-test order (a higher tier always wins over a lower one, regardless of screen position, see UiInputController's own doc comment).</summary>
    public static ReadOnlySpan<UiLayer> LayersDescending() => DescendingLayers;
}
