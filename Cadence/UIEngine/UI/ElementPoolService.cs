using Engine.Collections;
using System.Buffers;
using System.Reflection;

namespace UIEngine.UI;

/// <summary>Manages the pooling and construction of UI elements.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ElementPoolService
{
    private readonly Dictionary<Type, ObjectPool<Element>> _elementPoolsByType = [];

    /// <summary>Per-Type cache of every event's backing field across the whole Element/Window/... hierarchy for that Type -- see ClearEventSubscriptions.</summary>
    private readonly Dictionary<Type, FieldInfo[]> _eventBackingFieldsByType = [];

    /// <summary>Elements currently sitting in a pool, not yet rented back out.</summary>
    /// <remarks>
    /// ObjectPool.Return has no duplicate-return protection, and every element of a type shares one pool, so closing one
    /// instance twice would put it in the pool twice and hand it out to two different windows. CloseElement checks this set
    /// first. Reference equality is what's wanted: same instance, not same content.
    /// </remarks>
    private readonly HashSet<Element> _pooledElements = [];

    /// <summary>Registers the factory a type's pool creates new instances with.</summary>
    /// <remarks>The factory must create exactly TElement, not a subclass: an element goes back to the pool of its own type when it closes.</remarks>
    /// <exception cref="InvalidOperationException">TElement already has a factory.</exception>
    public void RegisterFactory<TElement>(Func<TElement> factory)
        where TElement : Element
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (_elementPoolsByType.ContainsKey(typeof(TElement)))
        {
            throw new InvalidOperationException($"{typeof(TElement).Name} already has an element factory; a type has one pool and one factory.");
        }

        _elementPoolsByType[typeof(TElement)] = new ObjectPool<Element>(() => CreateExactly(factory), static element => element.HideForPool());
    }

    /// <summary>Whether TElement has a factory registered.</summary>
    public bool HasFactory<TElement>() where TElement : Element => _elementPoolsByType.ContainsKey(typeof(TElement));

    private static TElement CreateExactly<TElement>(Func<TElement> factory) where TElement : Element
    {
        var element = factory();
        if (element.GetType() != typeof(TElement))
        {
            throw new InvalidOperationException($"The {typeof(TElement).Name} factory created a {element.GetType().Name}; register a factory for {element.GetType().Name} instead, so it returns to its own pool.");
        }

        return element;
    }

    /// <summary>Rents an element of type TElement from its pool and builds it with options under parent.</summary>
    /// <exception cref="InvalidOperationException">No factory is registered for TElement.</exception>
    public TElement CreateElement<TElement>(Element? parent, ElementOptions options)
        where TElement : Element
    {
        if (!_elementPoolsByType.TryGetValue(typeof(TElement), out var pool))
        {
            throw new InvalidOperationException($"No factory registered for element type {typeof(TElement).Name}. Call RegisterFactory first.");
        }

        var element = (TElement)pool.Rent();
        _pooledElements.Remove(element);
        element.Build(parent, options);
        return element;
    }

    /// <summary>Closes element and its whole subtree, returning each to its type's pool.</summary>
    /// <remarks>
    /// Children and title buttons close first, then every event subscription is cleared, so a pooled element never
    /// carries a handler into its next use. Closing an element that is already pooled does nothing. An element whose type
    /// has no pool (one constructed directly) is torn down the same way but kept by nobody.
    /// </remarks>
    public void CloseElement(Element element)
    {
        if (TryTearDown(element))
        {
            element.ParentElement?.RemoveChild(element);
        }
    }

    /// <summary>Closes element's subtree and returns it to its pool, leaving it in its parent's child list; false when it is already pooled.</summary>
    /// <remarks>Its own children are detached with no layout: the element is leaving, so nothing reads where they were.</remarks>
    private bool TryTearDown(Element element)
    {
        if (!_pooledElements.Add(element))
        {
            return false;
        }

        element.PoolReturnCount++;
        CloseAllTitleButtons(element);
        CloseAllChildren(element, parentRelaysOut: false);
        ClearEventSubscriptions(element);
        element.OnClosed();

        if (_elementPoolsByType.TryGetValue(element.GetType(), out var pool))
        {
            pool.Return(element);
        }
        else
        {
            _pooledElements.Remove(element);
        }

        return true;
    }

    /// <summary>Nulls every event field declared anywhere in element's type hierarchy before it goes back into its pool.</summary>
    /// <remarks>
    /// Reflection-based, so no element type has to remember to clear its own events: an element entering the pool is about
    /// to become a different logical widget. Finds the compiler's backing field of every field-like event; an event with
    /// custom add/remove accessors has none and isn't cleared. The fields are found once per type.
    /// </remarks>
    private void ClearEventSubscriptions(Element element)
    {
        foreach (var field in GetEventBackingFields(element.GetType()))
        {
            field.SetValue(element, null);
        }
    }

    private FieldInfo[] GetEventBackingFields(Type type)
    {
        if (_eventBackingFieldsByType.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var fields = new List<FieldInfo>();
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var eventInfo in current.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var backingField = current.GetField(eventInfo.Name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (backingField is not null)
                {
                    fields.Add(backingField);
                }
            }
        }

        var result = fields.ToArray();
        _eventBackingFieldsByType[type] = result;
        return result;
    }

    /// <summary>Closes every title button of parent, if it is a Window.</summary>
    /// <remarks>
    /// Title buttons live in Window's own list, not its children. The list itself isn't cleared here: this often runs
    /// inside a title button's own Clicked handler, while Window is still enumerating that list. Window.Build gives the
    /// next use a fresh list.
    /// </remarks>
    public void CloseAllTitleButtons(Element parent)
    {
        if (parent is Window window)
        {
            foreach (var titleButton in window.TitleButtons.ToArray())
            {
                CloseElement(titleButton);
            }
        }
    }

    /// <summary>Closes every child of parent, each returning to its own type's pool.</summary>
    /// <remarks>
    /// Each child leaves parent's child list as it is pooled, and parent lays itself out once, after the last, rather than
    /// once a child. A child added to parent while they close is not one of them and stays.
    /// </remarks>
    public void CloseAllChildren(Element parent) => CloseAllChildren(parent, parentRelaysOut: true);

    private void CloseAllChildren(Element parent, bool parentRelaysOut)
    {
        var children = parent.ChildElements;
        if (children.Count == 0)
        {
            return;
        }

        var closingChildren = ArrayPool<Element>.Shared.Rent(children.Count);
        var closingChildCount = children.Count;
        for (var childIndex = 0; childIndex < closingChildCount; childIndex++)
        {
            closingChildren[childIndex] = children[childIndex];
        }

        var detachedAny = false;
        try
        {
            for (var childIndex = 0; childIndex < closingChildCount; childIndex++)
            {
                var child = closingChildren[childIndex];
                var closedNow = TryTearDown(child);
                if ((closedNow || _pooledElements.Contains(child)) && parent.DetachChildWithoutLayout(child))
                {
                    detachedAny = true;
                }
            }
        }
        finally
        {
            Array.Clear(closingChildren, 0, closingChildCount);
            ArrayPool<Element>.Shared.Return(closingChildren);

            if (detachedAny)
            {
                parent.OnChildrenDetached(parentRelaysOut);
            }
        }
    }
}
