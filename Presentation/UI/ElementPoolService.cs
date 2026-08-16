using Engine.Collections;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI;

/// <summary>Manages the pooling and construction of UI elements.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ElementPoolService
{
    private readonly Dictionary<Type, ObjectPool<Element>> _elementPoolsByType = [];

    public ElementPoolService(FontService fontService, GlyphRenderer glyphRenderer)
    {
        ArgumentNullException.ThrowIfNull(fontService);
        ArgumentNullException.ThrowIfNull(glyphRenderer);

        RegisterFactory<Window>(() => new Window(fontService, this, glyphRenderer));
        RegisterFactory<TextWindow>(() => new TextWindow(fontService, this, glyphRenderer));
        RegisterFactory<TextBox>(() => new TextBox(fontService, this, glyphRenderer));
    }

    /// <summary>Registers a factory for creating instances of a specific element type. </summary>
    /// <typeparam name="TElement">The type of the element to create a factory for  .</typeparam>
    /// <param name="factory">The factory function to use for creating instances of the element type.</param>
    public void RegisterFactory<TElement>(Func<TElement> factory)
        where TElement : Element
    {
        ArgumentNullException.ThrowIfNull(factory);

        _elementPoolsByType[typeof(TElement)] = new ObjectPool<Element>(() => factory(), static element => element.IsVisible = false);
    }

    /// <summary>Creates an instance of the specified element type.</summary>
    /// <remarks>Rents an instance from the pool and then builds it.</remarks>
    /// <typeparam name="TElement">The type of the element to create.</typeparam>
    /// <param name="parent">The parent element.</param>
    /// <param name="options">The options for the element.</param>
    /// <returns>The created element.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no factory is registered for the element type.</exception>
    public TElement CreateElement<TElement>(Element? parent, ElementOptions options)
        where TElement : Element
    {
        if (!_elementPoolsByType.TryGetValue(typeof(TElement), out var pool))
        {
            throw new InvalidOperationException($"No factory registered for element type {typeof(TElement).Name}. Call RegisterFactory first.");
        }

        var element = (TElement)pool.Rent();
        element.Build(parent, options);
        return element;
    }

    /// <summary>Closes the specified element and returns it to its type pool.</summary>
    /// <param name="element">The element to close.</param>
    public void CloseElement(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (_elementPoolsByType.TryGetValue(element.GetType(), out var pool))
        {
            pool.Return(element);
        }

        element.ParentElement?.RemoveChild(element.ElementId);
    }

    /// <summary>Closes all child elements of the specified parent element.</summary>
    /// <remarks>Each child element is returned to its own type pool.</remarks>
    /// <param name="parent">The parent element.</param>
    public void CloseAllChildren(Element parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        foreach (var child in parent.ChildElements.ToArray())
        {
            CloseElement(child);
        }
    }
}
