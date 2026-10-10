using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Relationships;

namespace Engine.ECS.Components;

/// <summary>Registry tying entity ids to typed component pools. </summary>
/// <remarks>No component type is hardcoded here -- callers register whatever component types they own, keeping Engine free of any Game-specific knowledge.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ComponentManager
{
    private readonly int _initialEntityCapacity;
    private readonly int _initialComponentCapacity;

    private readonly Dictionary<Type, IComponentPool> _componentPools = [];
    private readonly Dictionary<Type, object> _relationshipsByLinkType = [];

    /// <summary>Initializes a new instance of the <see cref="ComponentManager"/> class.</summary>
    /// <param name="initialEntityCapacity">The initial capacity for indexing component pools based on the estimated number of entities with the component.</param>
    /// <param name="initialComponentCapacity">The dense storage a Packed or Multi pool starts with when its registration gives none; it grows geometrically from there.</param>
    public ComponentManager(int initialEntityCapacity, int initialComponentCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialEntityCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialComponentCapacity);

        _initialEntityCapacity = initialEntityCapacity;
        _initialComponentCapacity = initialComponentCapacity;
    }

    /// <summary>Every entity's stable key -- see EntityKeys. EntityManager issues and releases them; relationships resolve links through it.</summary>
    public EntityKeys Keys { get; } = new();

    /// <summary>Every relationship type registered here -- see RegisterRelationship.</summary>
    public RelationshipRegistry Relationships { get; } = new();

    /// <summary>The entities EntityManager.DestroyEntity is running for. EntityManager keeps it; relationships refuse them as targets and only detach them as sources.</summary>
    internal HashSet<int> DestroyingEntityIds { get; } = [];

    /// <summary>Returns true if the component type is registered to a pool.</summary>
    public bool IsRegistered<T>() where T : struct => _componentPools.ContainsKey(typeof(T));

    /// <summary>Registers a direct component pool for the specified component type.</summary>
    /// <remarks>Direct component pools are suitable for components that are expected to be present on most entities.</remarks>
    public void RegisterDirectPool<T>(MergeAction<T> mergeAction) where T : struct
    {
        ThrowIfAlreadyRegistered(typeof(T));
        _componentPools.Add(typeof(T), new DirectComponentPool<T>(_initialEntityCapacity, mergeAction));
    }

    /// <summary>Registers a packed component pool for the specified component type.</summary>
    /// <remarks>
    /// Packed component pools are suitable for components that are expected to be present on a subset of entities.
    /// Its entity index is paged, so it costs only the id ranges its holders fall in. initialCapacity is the dense
    /// storage it starts with (the manager's default when null); it grows geometrically from there, so a component
    /// known to be common can start larger to skip the early growth steps.
    /// </remarks>
    public void RegisterPackedPool<T>(MergeAction<T> mergeAction, int? initialCapacity = null) where T : struct
    {
        ThrowIfAlreadyRegistered(typeof(T));
        _componentPools.Add(typeof(T), new PackedComponentPool<T>(_initialEntityCapacity, initialCapacity ?? _initialComponentCapacity, mergeAction));
    }

    /// <summary>Registers a multi component pool for the specified component type.</summary>
    /// <remarks>
    /// Multi component pools are suitable for components that can be added multiple times to the same entity.
    /// See RegisterPackedPool's own remarks for initialCapacity.
    /// </remarks>
    public void RegisterMultiPool<T>(int? initialCapacity = null) where T : struct
    {
        ThrowIfAlreadyRegistered(typeof(T));
        _componentPools.Add(typeof(T), new MultiComponentPool<T>(_initialEntityCapacity, initialCapacity ?? _initialComponentCapacity));
    }

    /// <summary>Registers a relationship type: a Packed pool of TLink (the links) and a Multi pool of RelatedSourceComponent&lt;TLink&gt; (each target's sources), kept in step by a Relationship.</summary>
    /// <remarks>
    /// A source holds one link per relationship type, so writing a link replaces the one it had. The target
    /// side is written only by the relationship: GetMultiPool, Merge and RemoveComponent refuse it. See Relationship.
    /// initialCapacity sizes both pools' dense storage, as in RegisterPackedPool.
    /// </remarks>
    public Relationship<TLink> RegisterRelationship<TLink>(RelationshipSpec spec, int? initialCapacity = null) where TLink : struct, IRelationshipLink
    {
        ArgumentNullException.ThrowIfNull(spec);
        ThrowIfAlreadyRegistered(typeof(TLink));
        ThrowIfAlreadyRegistered(typeof(RelatedSourceComponent<TLink>));

        var links = new PackedComponentPool<TLink>(_initialEntityCapacity, initialCapacity ?? _initialComponentCapacity, static (ref existing, incoming) => existing = incoming);
        var relatedSources = new MultiComponentPool<RelatedSourceComponent<TLink>>(_initialEntityCapacity, initialCapacity ?? _initialComponentCapacity);
        _componentPools.Add(typeof(TLink), links);
        _componentPools.Add(typeof(RelatedSourceComponent<TLink>), relatedSources);

        var relationship = new Relationship<TLink>(spec, links, relatedSources, Keys, DestroyingEntityIds, _initialEntityCapacity);
        _relationshipsByLinkType.Add(typeof(TLink), relationship);
        Relationships.Add(relationship, typeof(TLink), typeof(RelatedSourceComponent<TLink>));
        return relationship;
    }

    /// <summary>Retrieves the relationship whose links are TLink.</summary>
    public Relationship<TLink> GetRelationship<TLink>() where TLink : struct, IRelationshipLink =>
        _relationshipsByLinkType.TryGetValue(typeof(TLink), out var relationship)
            ? (Relationship<TLink>)relationship
            : throw new InvalidOperationException($"No relationship with {typeof(TLink).Name} links is registered.");

    private void ThrowIfRelatedSourceComponentType(Type componentType)
    {
        if (Relationships.IsRelatedSourceComponentType(componentType))
        {
            throw new InvalidOperationException($"{componentType.Name} is kept by its relationship and can't be written directly; write the link, and read a target's sources through GetRelationship.");
        }
    }

    private void ThrowIfAlreadyRegistered(Type componentType)
    {
        if (_componentPools.ContainsKey(componentType))
        {
            throw new InvalidOperationException($"Component type {componentType.Name} is already registered.");
        }
    }

    /// <summary>Retrieves the direct component pool for the specified component type.</summary>
    /// <remarks>Used when the component is known to be registered to a direct pool.</remarks>
    public DirectComponentPool<T> GetDirectPool<T>() where T : struct
    {
        if (!_componentPools.TryGetValue(typeof(T), out var componentPool))
        {
            throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered.");
        }

        if (componentPool is not DirectComponentPool<T> typedStore)
        {
            throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered as a direct component pool.");
        }

        return typedStore;
    }

    /// <summary>Retrieves the packed component pool for the specified component type.</summary>
    /// <remarks>Used when the component is known to be registered to a packed pool.</remarks>
    public PackedComponentPool<T> GetPackedPool<T>() where T : struct
    {
        if (!_componentPools.TryGetValue(typeof(T), out var componentPool))
        {
            throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered.");
        }

        if (componentPool is not PackedComponentPool<T> typedStore)
        {
            throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered as a packed component pool.");
        }

        return typedStore;
    }

    /// <summary>Retrieves the multi component pool for the specified component type.</summary>
    /// <remarks>Used when the component is known to be registered to a multi pool.</remarks>
    public MultiComponentPool<T> GetMultiPool<T>() where T : struct
    {
        ThrowIfRelatedSourceComponentType(typeof(T));

        if (!_componentPools.TryGetValue(typeof(T), out var componentPool))
        {
            throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered.");
        }

        if (componentPool is not MultiComponentPool<T> typedStore)
        {
            throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered as a multi component pool.");
        }

        return typedStore;
    }

    /// <summary>Retrieves the read-only component pool for the specified component type regardless of its registration type.</summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public IReadOnlyComponentPool<T> GetReadOnlyPool<T>() where T : struct
    {
        if (!_componentPools.TryGetValue(typeof(T), out var store))
        {
            throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered.");
        }

        return (IReadOnlyComponentPool<T>)store;
    }

    /// <summary> Adds or merges a component without the caller needing to know which pool type T was registered as</summary>
    /// <remarks>Direct and Packed pools merge with any existing component; Multi pools have no single existing value to merge into, so every call is an Add.
    public void Merge<T>(int entityId, T component) where T : struct
    {
        ThrowIfRelatedSourceComponentType(typeof(T));

        if (!_componentPools.TryGetValue(typeof(T), out var pool))
        {
            throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered.");
        }

        switch (pool)
        {
            case DirectComponentPool<T> direct:
                direct.Merge(entityId, component);
                break;
            case PackedComponentPool<T> packed:
                packed.Merge(entityId, component);
                break;
            case MultiComponentPool<T> multi:
                multi.Add(entityId, component);
                break;
            default:
                throw new InvalidOperationException($"Component type {typeof(T).Name} is registered as an unsupported pool type for Merge.");
        }
    }

    /// <summary>
    /// Mutates an existing component without the caller needing to know which pool type T was registered as. </summary> 
    /// <remarks>
    /// Returns false if the entity has no component of type T. 
    /// Multi pools have no single existing value to update (an entity may have 0..N) and are not supported here -- use GetMultiPool&lt;T&gt;().TryUpdateFirst directly.
    /// </remarks>
    public bool TryUpdate<T>(int entityId, ComponentUpdater<T> updater) where T : struct
    {
        if (!_componentPools.TryGetValue(typeof(T), out var pool))
        {
            throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered.");
        }

        return pool switch
        {
            DirectComponentPool<T> direct => direct.TryUpdate(entityId, updater),
            PackedComponentPool<T> packed => packed.TryUpdate(entityId, updater),
            _ => throw new InvalidOperationException($"Component type {typeof(T).Name} is registered as an unsupported pool type for TryUpdate."),
        };
    }

    /// <summary> All registered component pools</summary>
    /// <remarks> For inspection tooling (e.g. Diagnostics/ComponentInspector). </remarks>
    public Dictionary<Type, IComponentPool>.ValueCollection AllPools => _componentPools.Values;

    /// <summary>Grows every pool to accept entity ids below newMaximumEntityCount.</summary>
    /// <remarks>Direct pools reallocate to the new capacity; Packed and Multi pools only grow their page table, allocating no pages.</remarks>
    public void ResizeEntityCapacity(int newMaximumEntityCount)
    {
        foreach (var componentPool in _componentPools.Values)
        {
            componentPool.Resize(newMaximumEntityCount);
        }
    }

    /// <summary>Removes a component of the specified type from the entity</summary>
    public bool RemoveComponent<T>(int entityId) where T : struct
    {
        ThrowIfRelatedSourceComponentType(typeof(T));

        if (!_componentPools.TryGetValue(typeof(T), out var componentPool))
        {
            throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered.");
        }

        return componentPool.Remove(entityId);
    }

    /// <summary>Removes all components in all pools from the entity</summary>
    /// <param name="entityId"></param>
    /// <summary>Grows every pool's dense storage, where smaller, to hold its current count times factor, so that much growth later never reallocates mid-game.</summary>
    /// <remarks>For a population known to grow by about that much at runtime: loading ahead of time what would otherwise be a run of reallocate-and-copy passes, each one frame's hitch.</remarks>
    public void ReserveHeadroom(double factor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 1d);

        ReserveHeadroom(_ => factor);
    }

    /// <summary>ReserveHeadroom with a factor per component type, for pools whose populations grow by different amounts.</summary>
    public void ReserveHeadroom(Func<Type, double> factorFor)
    {
        foreach (var componentPool in _componentPools.Values)
        {
            if (componentPool is IMemoryReportingComponentPool { Count: > 0 } counted)
            {
                var factor = factorFor(componentPool.ComponentType);
                ArgumentOutOfRangeException.ThrowIfLessThan(factor, 1d);
                componentPool.ReserveDenseCapacity((int)System.Math.Ceiling(counted.Count * factor));
            }
        }
    }

    public void RemoveAllComponents(int entityId)
    {
        foreach (var componentPool in _componentPools.Values)
        {
            componentPool.Remove(entityId);
        }
    }
}