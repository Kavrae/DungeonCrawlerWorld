namespace Engine.ECS.Components.Stores;

/// <summary>
/// Entity-indexed component storage for near-universal components. Storage index ==
/// entityId. Best for components present on most entities, where direct lookup beats
/// sparse-set indirection.
/// </summary>
public sealed class DirectComponentPool<T> : IReadOnlyComponentPool<T>, IInspectableComponentPool where T : struct
{
    public Type ComponentType => typeof(T);
    public ComponentPoolType ComponentPoolType => ComponentPoolType.Direct;

    private T[] _components;
    private byte[] _present;
    private uint[] _versions;
    private readonly MergeAction<T> _mergeImplementation;

    private int _count;
    public int Count => _count;
    public int Capacity => _components.Length;

    public ReadOnlySpan<T> Components => _components;
    public ReadOnlySpan<byte> Present => _present;
    public ReadOnlySpan<uint> Versions => _versions;

    public delegate void ComponentUpdater<TState>(ref T component, TState state);

    public DirectComponentPool(int initialCapacity, MergeAction<T> mergeImplementation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        ArgumentNullException.ThrowIfNull(mergeImplementation);

        _components = new T[initialCapacity];
        _present = new byte[initialCapacity];
        _versions = new uint[initialCapacity];
        _mergeImplementation = mergeImplementation;
        _count = 0;
    }

    public void Resize(int newMaximumEntityCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(newMaximumEntityCount, _components.Length);

        var oldLength = _components.Length;

        Array.Resize(ref _components, newMaximumEntityCount);
        Array.Resize(ref _present, newMaximumEntityCount);
        Array.Resize(ref _versions, newMaximumEntityCount);

        for (var i = oldLength; i < newMaximumEntityCount; i++)
        {
            _present[i] = 0;
            _versions[i] = 0;
        }
    }

    public void Add(int entityId, T newComponent)
    {
        ValidateEntityId(entityId);

        if (_present[entityId] != 0)
        {
            throw new InvalidOperationException($"Entity {entityId} already has a component of type {typeof(T).Name}.");
        }

        _components[entityId] = newComponent;
        _present[entityId] = 1;
        _versions[entityId] = 1;
        _count++;
    }

    public void Merge(int entityId, T newComponent)
    {
        ValidateEntityId(entityId);

        if (_present[entityId] != 0)
        {
            _mergeImplementation(ref _components[entityId], newComponent);
            _versions[entityId]++;
            return;
        }

        Add(entityId, newComponent);
    }

    public bool Has(int entityId)
    {
        ValidateEntityId(entityId);

        return _present[entityId] != 0;
    }

    public bool TryGetReadonly(int entityId, out T component)
    {
        ValidateEntityId(entityId);

        if (_present[entityId] == 0)
        {
            component = default;
            return false;
        }

        component = _components[entityId];
        return true;
    }

    public ref readonly T GetReadonly(int entityId)
    {
        ValidateEntityId(entityId);

        if (_present[entityId] == 0)
        {
            throw new InvalidOperationException($"Entity {entityId} does not have component {typeof(T).Name}.");
        }

        return ref _components[entityId];
    }

    /// <summary>
    /// Hot-path mutable access. Caller must manually increment version after mutation.
    /// Prefer TryUpdate/TrySet unless you are in a tight loop.
    /// </summary>
    public ref T Get(int entityId)
    {
        ValidateEntityId(entityId);

        if (_present[entityId] == 0)
        {
            throw new InvalidOperationException($"Entity {entityId} does not have component {typeof(T).Name}.");
        }

        return ref _components[entityId];
    }

    public int CopyInspectionDataForEntity(int entityId, List<InspectedComponentEntry> destination)
    {
        ValidateEntityId(entityId);

        if (!Has(entityId))
        {
            return 0;
        }

        destination.Add(new InspectedComponentEntry(
            ComponentType,
            ComponentPoolType,
            GetReadonly(entityId),
            GetVersion(entityId)));

        return 1;
    }

    public uint GetVersion(int entityId)
    {
        ValidateEntityId(entityId);

        if (_present[entityId] == 0)
        {
            throw new InvalidOperationException($"Entity {entityId} does not have component {typeof(T).Name}.");
        }

        return _versions[entityId];
    }

    public bool TrySet(int entityId, T value)
    {
        ValidateEntityId(entityId);

        if (_present[entityId] == 0)
        {
            return false;
        }

        _components[entityId] = value;
        _versions[entityId]++;
        return true;
    }

    public bool TryUpdate(int entityId, Engine.ECS.Components.ComponentUpdater<T> updater)
    {
        ValidateEntityId(entityId);
        ArgumentNullException.ThrowIfNull(updater);

        if (_present[entityId] == 0)
        {
            return false;
        }

        updater(ref _components[entityId]);
        _versions[entityId]++;
        return true;
    }

    public bool TryUpdate<TState>(int entityId, TState state, ComponentUpdater<TState> updater)
    {
        ValidateEntityId(entityId);
        ArgumentNullException.ThrowIfNull(updater);

        if (_present[entityId] == 0)
        {
            return false;
        }

        updater(ref _components[entityId], state);
        _versions[entityId]++;
        return true;
    }

    public void IncrementVersion(int entityId)
    {
        ValidateEntityId(entityId);

        if (_present[entityId] == 0)
        {
            throw new InvalidOperationException($"Entity {entityId} does not have component {typeof(T).Name}.");
        }

        _versions[entityId]++;
    }

    public bool Remove(int entityId)
    {
        ValidateEntityId(entityId);

        if (_present[entityId] == 0)
        {
            return false;
        }

        _components[entityId] = default;
        _present[entityId] = 0;
        _versions[entityId] = 0;
        _count--;
        return true;
    }

    public void Clear()
    {
        Array.Clear(_components);
        Array.Clear(_present);
        Array.Clear(_versions);
        _count = 0;
    }

    private void ValidateEntityId(int entityId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(entityId, _components.Length);
    }
}
