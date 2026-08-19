namespace Presentation.UI;

/// <summary>
/// Owns every root Element the shell draws/updates/hit-tests, grouped by UiLayer -- replaces
/// what used to be four independently-hardcoded List&lt;Element&gt; fields (Base/StaticHud/
/// DynamicHud/User) threaded individually through GameShellBootstrapper/UiInputController/
/// NotificationCenter/InventoryFolderController/HotbarController with one structure keyed by an
/// enum whose declaration order defines z-order. A window is now added by naming its UiLayer
/// (layers.Add(UiLayer.DynamicHud, window)) rather than a caller being handed a reference to
/// whichever specific list happens to represent that tier.
/// </summary>
public sealed class UiLayerStack
{
    private readonly Dictionary<UiLayer, List<Element>> _byLayer;

    /// <summary>Every UiLayer starts as its own new, empty list.</summary>
    public UiLayerStack()
    {
        _byLayer = new Dictionary<UiLayer, List<Element>>();
        foreach (var layer in Enum.GetValues<UiLayer>())
        {
            _byLayer[layer] = [];
        }
    }

    public IReadOnlyList<Element> this[UiLayer layer] => _byLayer[layer];

    public void Add(UiLayer layer, Element element) => _byLayer[layer].Add(element);

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
