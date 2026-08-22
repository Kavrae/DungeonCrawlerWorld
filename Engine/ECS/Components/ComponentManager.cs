using Engine.ECS.Components.Stores;

namespace Engine.ECS.Components;

/// <summary>Registry tying entity ids to typed component pools. </summary>
/// <remarks>No component type is hardcoded here -- callers register whatever component types they own, keeping Engine free of any Game-specific knowledge.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ComponentManager
{
    private readonly int _initialEntityCapacity;
    private readonly int _initialComponentCapacity;

    private readonly Dictionary<Type, IComponentPool> _componentPools = [];
    private readonly HashSet<Type> _independentlySizedTypes = [];

    /// <summary>Initializes a new instance of the <see cref="ComponentManager"/> class.</summary>
    /// <param name="initialEntityCapacity">The initial capacity for indexing component pools based on the estimated number of entities with the component.</param>
    /// <param name="initialComponentCapacity">The initial capacity for dense component storage (packed and multi), based on the estimated number of total components in the pool.</param>
    public ComponentManager(int initialEntityCapacity, int initialComponentCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialEntityCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialComponentCapacity);

        _initialEntityCapacity = initialEntityCapacity;
        _initialComponentCapacity = initialComponentCapacity;
    }

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
    /// maximumEntityCount/initialCapacity default to the manager's own world-scale fields; pass smaller values for a
    /// component known to be rare. initialCapacity (dense/instance-count storage) is always safe to shrink on its own
    /// -- it already grows independently as needed (see PackedComponentPool.EnsureDenseCapacityForOneMore) regardless
    /// of ResizeEntityCapacity. maximumEntityCount (entity-index storage) additionally opts the pool out of
    /// ResizeEntityCapacity's uniform sweep -- pass it only when the component realistically can't land on just any
    /// entity in the world (e.g. Player-only), since the pool still grows itself on demand (see PackedComponentPool.Add)
    /// if an entity id beyond the seed is ever actually used.
    /// </remarks>
    public void RegisterPackedPool<T>(MergeAction<T> mergeAction, int? maximumEntityCount = null, int? initialCapacity = null) where T : struct
    {
        ThrowIfAlreadyRegistered(typeof(T));
        _componentPools.Add(typeof(T), new PackedComponentPool<T>(maximumEntityCount ?? _initialEntityCapacity, initialCapacity ?? _initialComponentCapacity, mergeAction));

        if (maximumEntityCount is not null)
        {
            _independentlySizedTypes.Add(typeof(T));
        }
    }

    /// <summary>Registers a multi component pool for the specified component type.</summary>
    /// <remarks>
    /// Multi component pools are suitable for components that can be added multiple times to the same entity.
    /// See RegisterPackedPool's own remarks for maximumEntityCount/initialCapacity -- the same optional-override,
    /// grow-on-demand behavior applies here.
    /// </remarks>
    /// <typeparam name="T"></typeparam>
    public void RegisterMultiPool<T>(int? maximumEntityCount = null, int? initialCapacity = null) where T : struct
    {
        ThrowIfAlreadyRegistered(typeof(T));
        _componentPools.Add(typeof(T), new MultiComponentPool<T>(maximumEntityCount ?? _initialEntityCapacity, initialCapacity ?? _initialComponentCapacity));

        if (maximumEntityCount is not null)
        {
            _independentlySizedTypes.Add(typeof(T));
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

    /// <remarks>
    /// Skips any pool registered with an explicit maximumEntityCount/initialCapacity override (see
    /// RegisterPackedPool/RegisterMultiPool) -- those pools are deliberately smaller than the world's full
    /// entity id space and grow themselves on demand instead of being forced back up to newMaximumEntityCount
    /// here every time some unrelated entity creation grows the global capacity.
    /// </remarks>
    public void ResizeEntityCapacity(int newMaximumEntityCount)
    {
        foreach (var (type, componentPool) in _componentPools)
        {
            if (_independentlySizedTypes.Contains(type))
            {
                continue;
            }

            componentPool.Resize(newMaximumEntityCount);
        }
    }

    /// <summary>Removes a component of the specified type from the entity</summary>
    public bool RemoveComponent<T>(int entityId) where T : struct
    {
        if (!_componentPools.TryGetValue(typeof(T), out var componentPool))
        {
            throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered.");
        }

        return componentPool.Remove(entityId);
    }

    /// <summary>Removes all components in all pools from the entity</summary>
    /// <param name="entityId"></param>
    public void RemoveAllComponents(int entityId)
    {
        foreach (var componentPool in _componentPools.Values)
        {
            componentPool.Remove(entityId);
        }
    }
}