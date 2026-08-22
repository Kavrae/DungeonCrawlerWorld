using Engine.Collections;
using Microsoft.Xna.Framework.Graphics;
using System.Reflection;

namespace Presentation.UI;

/// <summary>Manages the pooling and construction of UI elements.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ElementPoolService
{
    private readonly Dictionary<Type, ObjectPool<Element>> _elementPoolsByType = [];

    /// <summary>
    /// The render services every Element's Draw/DrawContent/DrawHeader needs -- set once (see
    /// Initialize) and read through this shared, already-constructed service by every Element,
    /// pooled or freshly created, at any point in the session. Unlike ShellContext (one
    /// long-lived instance with a real one-time LoadContent hook), Element instances are
    /// constantly created/pooled/reused throughout the session -- most are never LoadContent'd
    /// individually (Element.LoadContent is only ever invoked, once, on the fixed set of root
    /// windows ShellContext.LoadContent walks at startup; nothing calls it again for a
    /// window opened later, e.g. Inventory) -- so caching these directly on Element itself,
    /// the same way ShellContext caches its own copies, would leave every dynamically
    /// created window's fields unset. Routing through the one ElementPoolService reference every
    /// Element already holds from construction (see Element's own constructor) sidesteps that:
    /// it doesn't matter when an Element was constructed relative to Initialize below, only that
    /// Initialize has run by the time anything is ever drawn, which the real GameLoop lifecycle
    /// (Initialize/LoadContent before the first Update/Draw) already guarantees.
    /// </summary>
    public GraphicsDevice GraphicsDevice { get; private set; } = null!;

    public SpriteBatch SpriteBatch { get; private set; } = null!;

    public Texture2D UnitRectangle { get; private set; } = null!;

    /// <summary>Captures the render services above -- called once, from GameLoop.LoadContent, after all three actually exist.</summary>
    public void Initialize(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        GraphicsDevice = graphicsDevice;
        SpriteBatch = spriteBatch;
        UnitRectangle = unitRectangle;
    }

    /// <summary>Per-Type cache of every event's backing field across the whole Element/Window/... hierarchy for that Type -- see ClearEventSubscriptions.</summary>
    private readonly Dictionary<Type, FieldInfo[]> _eventBackingFieldsByType = [];

    /// <summary>
    /// Elements currently sitting in a pool, not yet rented back out -- see CloseElement's own
    /// guard. ObjectPool&lt;T&gt;.Return pushes unconditionally with no duplicate-return
    /// protection, and every Window-typed parent (tab header strip, tab body, GridControl's own
    /// host window, ...) rents from the SAME shared Stack&lt;Element&gt;. If CloseElement were
    /// ever invoked twice on the same instance within one close cascade, that instance would land
    /// in the stack twice, and two subsequen
    /// t CreateElement&lt;Window&gt; calls could then hand
    /// out the identical object to two different logical windows -- the second Build() overwrites
    /// the first's geometry, AddChild collapses onto the same child slot, and SetContent on the
    /// shared instance immediately closes whatever the first one had just added as children.
    /// Reference equality (Element has no custom Equals) is exactly what's wanted here: same
    /// instance, not same content.
    /// </summary>
    private readonly HashSet<Element> _pooledElements = [];

    /// <summary>Registers a factory for creating instances of a specific parent type. </summary>
    /// <typeparam name="TElement">The type of the parent to create a factory for  .</typeparam>
    /// <param name="factory">The factory function to use for creating instances of the parent type.</param>
    public void RegisterFactory<TElement>(Func<TElement> factory)
        where TElement : Element
    {
        ArgumentNullException.ThrowIfNull(factory);

        _elementPoolsByType[typeof(TElement)] = new ObjectPool<Element>(() => factory(), static element => element.IsVisible = false);
    }

    /// <summary>Creates an instance of the specified parent type.</summary>
    /// <remarks>Rents an instance from the pool and then builds it.</remarks>
    /// <typeparam name="TElement">The type of the parent to create.</typeparam>
    /// <param name="parent">The parent parent.</param>
    /// <param name="options">The options for the parent.</param>
    /// <returns>The created parent.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no factory is registered for the parent type.</exception>
    public TElement CreateElement<TElement>(Element? parent, ElementOptions options)
        where TElement : Element
    {
        if (!_elementPoolsByType.TryGetValue(typeof(TElement), out var pool))
        {
            throw new InvalidOperationException($"No factory registered for parent type {typeof(TElement).Name}. Call RegisterFactory first.");
        }

        var element = (TElement)pool.Rent();
        _pooledElements.Remove(element);
        element.Build(parent, options);
        return element;
    }

    /// <summary>
    /// Closes the specified parent -- recursively closing its own children first (see
    /// CloseAllChildren, mutually recursive with this method) -- and returns it to its type pool.
    /// Recursive so closing any parent tears down and pool-returns its entire subtree, not just
    /// itself: without this, a control that owns pooled children of its own (GridControl's count/
    /// sort/toggle/search-box children, not just its own Window-level events) would leave those
    /// grandchildren orphaned -- still attached in the tree, never returned to their own pools --
    /// whenever whatever closed it only reached the direct child level (confirmed bug: the
    /// Inventory item search box duplicating itself, old orphaned copy still rendering behind a
    /// freshly-created one once GridControl got rented again for another tab).
    ///
    /// Guarded by _pooledElements against being invoked twice on the same instance -- see that
    /// field's own doc comment for why an unguarded double-close is dangerous (shared-pool
    /// corruption), not merely wasteful.
    /// </summary>
    /// <param name="element">The parent to close.</param>
    public void CloseElement(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (!_pooledElements.Add(element))
        {
            return;
        }

        CloseAllTitleButtons(element);
        CloseAllChildren(element);
        ClearEventSubscriptions(element);
        element.OnClosed();

        if (_elementPoolsByType.TryGetValue(element.GetType(), out var pool))
        {
            pool.Return(element);
        }

        element.ParentElement?.RemoveChild(element.ElementId);
    }

    /// <summary>
    /// Nulls out every event field declared anywhere in parent's own type hierarchy --
    /// Element's own Opened/Closed/Resized/Moved/Clicked/DisplayModeChanged/FocusRequested/
    /// FocusChanged, plus whatever additional events a subclass declares (GridControl's
    /// SortOptionCycled/ToggleChanged/SearchFilterChanged, TextBox's TextSubmitted, ...) --
    /// before it goes back into its type pool.
    ///
    /// Closing an parent used to never clear its subscriptions at all, which made every pooled
    /// Element/Window individually responsible for remembering to unsubscribe from its own
    /// children's events before closing them -- easy to get right once (TabbedContent's tab
    /// tiles) and then forget the exact same pattern on the next control built the same way
    /// (GridControl's sort/toggle buttons: confirmed bug, clicks cross-wired between Inventory
    /// tabs once enough TextWindow instances had cycled through the shared pool with stale
    /// handlers still attached). Doing this once, centrally, here, makes the whole bug class
    /// structurally impossible for every current and future pooled Element, rather than a
    /// convention each new widget has to remember to reapply by hand -- an parent entering the
    /// pool is, by definition, about to potentially become a completely different logical widget
    /// the next time it's rented, so nothing should still be listening to what it does past this
    /// point regardless.
    ///
    /// Reflection-based rather than a virtual "ClearOwnEvents" override every subclass would have
    /// to remember to implement (and call base on) -- the same "easy to forget" failure mode this
    /// exists to eliminate. Every event in this codebase today is a plain auto-implemented
    /// `event Action? Foo` with no custom add/remove, which the C# compiler backs with a private
    /// field of the same name -- GetEvents(DeclaredOnly) walked up the type hierarchy, paired
    /// with GetField(NonPublic | Instance) at each level, finds them all. A future event declared
    /// with explicit custom add/remove accessors wouldn't have a same-named backing field to find
    /// this way -- GetField returning null for it is a silent no-op, not a crash, so this
    /// degrades safely rather than blowing up, but such an event's subscriptions wouldn't
    /// actually get cleared. Cached per Type (the reflection walk only runs once per distinct
    /// pooled Element type, not once per instance) so this stays cheap on the CloseElement path.
    /// </summary>
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
    // Title buttons live in Window's own _titleButtons list, not _children (see
    // Window.AddTitleButton's own doc comment for why) -- CloseAllChildren below only ever
    // walks _children, so without this a window's title buttons would never be returned to
    // Button's pool when the window closes, now that Button is itself pooled. Deliberately
    // does NOT also clear window.TitleButtons here -- this often runs while a title button's
    // own Clicked handler (e.g. the close button) is still unwinding up the call stack,
    // inside Window.OnHeaderClickAction's own (non-snapshotted) foreach over that exact list;
    // mutating it here would corrupt that still-active enumeration. Window.Build already
    // resets _titleButtons to a fresh list the next time this window is rented, so the stale
    // reference is harmless in the meantime.
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

    /// <summary>Closes all child elements of the specified parent parent.</summary>
    /// <remarks>Each child parent is returned to its own type pool.</remarks>
    /// <param name="parent">The parent parent.</param>
    public void CloseAllChildren(Element parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        foreach (var child in parent.ChildElements.ToArray())
        {
            CloseElement(child);
        }
    }
}
