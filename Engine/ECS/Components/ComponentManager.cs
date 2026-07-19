using Engine.ECS.Components.Stores;

namespace Engine.ECS.Components;

/// <summary>
/// Registry tying entity ids to typed component pools. No component type is hardcoded here
/// -- callers (module registration, via Bootstrapper) register whatever component types they
/// own, keeping Engine free of any Game-specific knowledge.
/// </summary>
public sealed class ComponentManager
{
    private readonly int _initialEntityCapacity;
    private readonly int _initialComponentCapacity;

    private readonly Dictionary<Type, IComponentPool> _componentPools = [];
    private readonly Dictionary<Type, ComponentPoolType> _componentPoolTypes = [];

    public ComponentManager(int initialEntityCapacity, int initialComponentCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialEntityCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialComponentCapacity);

        _initialEntityCapacity = initialEntityCapacity;
        _initialComponentCapacity = initialComponentCapacity;
    }

    public bool IsRegistered<T>() where T : struct => _componentPoolTypes.ContainsKey(typeof(T));

    public void RegisterDirectPool<T>(MergeAction<T> mergeAction) where T : struct
    {
        RegisterComponentPoolType(typeof(T), ComponentPoolType.Direct);
        _componentPools.Add(typeof(T), new DirectComponentPool<T>(_initialEntityCapacity, mergeAction));
    }

    public void RegisterPackedPool<T>(MergeAction<T> mergeAction) where T : struct
    {
        RegisterComponentPoolType(typeof(T), ComponentPoolType.Packed);
        _componentPools.Add(typeof(T), new PackedComponentPool<T>(_initialEntityCapacity, _initialComponentCapacity, mergeAction));
    }

    public void RegisterMultiPool<T>() where T : struct
    {
        RegisterComponentPoolType(typeof(T), ComponentPoolType.Multi);
        _componentPools.Add(typeof(T), new MultiComponentPool<T>(_initialEntityCapacity, _initialComponentCapacity));
    }

    private void RegisterComponentPoolType(Type componentType, ComponentPoolType componentPoolType)
    {
        if (_componentPools.ContainsKey(componentType))
        {
            throw new InvalidOperationException($"Component type {componentType.Name} is already registered.");
        }

        _componentPoolTypes.Add(componentType, componentPoolType);
    }

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

    public IReadOnlyComponentPool<T> GetReadOnlyPool<T>() where T : struct
    {
        if (!_componentPools.TryGetValue(typeof(T), out var store))
        {
            throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered.");
        }

        return (IReadOnlyComponentPool<T>)store;
    }

    public ComponentPoolType GetPoolType<T>() where T : struct
    {
        if (!_componentPoolTypes.TryGetValue(typeof(T), out var componentPoolType))
        {
            throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered.");
        }

        return componentPoolType;
    }

    /// <summary>
    /// Adds or merges a component without the caller needing to know which pool type T was
    /// registered as -- Direct and Packed pools merge with any existing component; Multi
    /// pools have no single existing value to merge into, so every call is an Add there.
    /// </summary>
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
    /// Mutates an existing component without the caller needing to know which pool type T was
    /// registered as. Returns false if the entity has no component of type T. Multi pools
    /// have no single existing value to update (an entity may have 0..N) and are not
    /// supported here -- use GetMultiPool&lt;T&gt;().TryUpdateFirst directly for those.
    /// </summary>
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

    /// <summary>All registered pools, for inspection tooling (e.g. Diagnostics/ComponentInspector).</summary>
    public IEnumerable<IComponentPool> AllPools => _componentPools.Values;

    public void ResizeEntityCapacity(int newMaximumEntityCount)
    {
        foreach (var componentPool in _componentPools.Values)
        {
            componentPool.Resize(newMaximumEntityCount);
        }
    }

    public bool RemoveComponent<T>(int entityId) where T : struct
    {
        if (!_componentPools.TryGetValue(typeof(T), out var componentPool))
        {
            throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered.");
        }

        return componentPool.Remove(entityId);
    }

    public void RemoveAllComponents(int entityId)
    {
        foreach (var componentPool in _componentPools.Values)
        {
            componentPool.Remove(entityId);
        }
    }
}
