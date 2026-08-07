using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI;

/// <summary>
/// Pools and constructs elements by type. CreateElement invokes the registered factory for a
/// type when the pool is empty, which is what lets Window/Folder/etc. take constructor-injected
/// dependencies instead of pulling them from a locator, since factories close over
/// </summary>
public sealed class ElementPoolService
{
    private readonly Dictionary<Type, Stack<Element>> _elementPoolsByType = [];
    private readonly Dictionary<Type, Func<Element?, ElementOptions, Element>> _elementFactoriesByType = [];

    private const int DefaultPoolGrowthSize = 8;
    private const int PoolMaximumSize = byte.MaxValue;

    public ElementPoolService(FontService fontService, GlyphRenderer glyphRenderer)
    {
        ArgumentNullException.ThrowIfNull(fontService);
        ArgumentNullException.ThrowIfNull(glyphRenderer);

        RegisterFactory<Window>((_, _) => new Window(fontService, this, glyphRenderer));
        RegisterFactory<TextWindow>((_, _) => new TextWindow(fontService, this, glyphRenderer));
        RegisterFactory<TextBox>((_, _) => new TextBox(fontService, this, glyphRenderer));
    }

    /// <summary>
    /// Not generic over an options type: ElementOptions composes independent option groups
    /// (see ElementOptions/ElementLayoutOptions/etc.) instead of being subclassed per window
    /// type, so every window type's factory takes the same ElementOptions.
    /// </summary>
    public void RegisterFactory<TElement>(Func<Element?, ElementOptions, TElement> factory)
        where TElement : Element
    {
        ArgumentNullException.ThrowIfNull(factory);

        _elementFactoriesByType[typeof(TElement)] = factory;
        _elementPoolsByType.TryAdd(typeof(TElement), new Stack<Element>(DefaultPoolGrowthSize));
    }

    public TElement CreateElement<TElement>(Element? parent, ElementOptions options)
        where TElement : Element
    {
        TElement element;
        if (_elementPoolsByType.TryGetValue(typeof(TElement), out var pool) && pool.Count > 0)
        {
            element = (TElement)pool.Pop();
        }
        else
        {
            if (!_elementFactoriesByType.TryGetValue(typeof(TElement), out var factory))
            {
                throw new InvalidOperationException($"No factory registered for element type {typeof(TElement).Name}. Call RegisterFactory first.");
            }

            element = (TElement)factory(parent, options);
        }

        element.Build(parent, options);
        return element;
    }

    public void CloseElement(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (_elementPoolsByType.TryGetValue(element.GetType(), out var pool) && pool.Count < PoolMaximumSize)
        {
            element.IsVisible = false;
            pool.Push(element);
        }

        element.ParentElement?.RemoveChild(element.ElementId);
    }

    /// <summary>
    /// Closes (returns to their own type pool, per CloseElement above) every current child of
    /// parent -- the "destroy-all, rebuild-fresh" idiom several content panes use when their
    /// backing data changes (InventoryGridContent's item cells, AbilityScoreWindow's columns/
    /// rows). Snapshots ChildElements first: CloseElement mutates parent's own child list as it
    /// goes (via RemoveChild), which would corrupt an in-progress enumeration of that same list.
    /// </summary>
    public void CloseAllChildren(Element parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        foreach (var child in parent.ChildElements.ToArray())
        {
            CloseElement(child);
        }
    }
}
