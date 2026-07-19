namespace Engine.ECS.Components.Stores;

/// <summary>
/// Sparse-set component storage for rare components, where a direct pool would waste
/// index space. Dense storage grows linearly (not exponentially) to bound peak memory
/// for components most entities never have.
/// </summary>
public sealed class PackedComponentPool<T> : IReadOnlyComponentPool<T>, IInspectableComponentPool where T : struct
{
    public Type ComponentType => typeof(T);
    public ComponentPoolType ComponentPoolType => ComponentPoolType.Packed;

    private int _maxEntities;
    private int[] _entityIdToDenseIndexMap;
    private int[] _denseIndexToEntityIdMap;
    private T[] _denseComponents;
    private uint[] _denseVersions;
    private readonly int _denseGrowthAmount;
    private readonly MergeAction<T> _mergeImplementation;

    private int _count;
    public int Count => _count;

    public ReadOnlySpan<T> Components => new(_denseComponents, 0, _count);
    public ReadOnlySpan<int> EntityIds => new(_denseIndexToEntityIdMap, 0, _count);
    public ReadOnlySpan<uint> Versions => new(_denseVersions, 0, _count);

    public delegate void ComponentUpdater<TState>(ref T component, TState state);

    /// <summary>
    /// Fired at the end of Add (including Merge's fallback-to-Add path) and Remove. Lets
    /// consumers (e.g. EntityStripeSet) maintain an entityId-keyed view of this pool's
    /// membership that stays correct across Remove's swap-with-last dense-index reshuffling,
    /// instead of re-deriving membership from live dense indices, which are not stable
    /// identifiers for an entity across time under churn.
    /// </summary>
    public event Action<int>? EntityAdded;
    public event Action<int>? EntityRemoved;

    public PackedComponentPool(int maximumEntityCount, int initialCapacity, MergeAction<T> mergeImplementation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        ArgumentNullException.ThrowIfNull(mergeImplementation);

        _maxEntities = maximumEntityCount;
        _entityIdToDenseIndexMap = new int[_maxEntities];
        Array.Fill(_entityIdToDenseIndexMap, -1);

        _denseComponents = new T[initialCapacity];
        _denseVersions = new uint[initialCapacity];
        _denseIndexToEntityIdMap = new int[initialCapacity];
        Array.Fill(_denseIndexToEntityIdMap, -1);

        _mergeImplementation = mergeImplementation;
        _denseGrowthAmount = initialCapacity;
        _count = 0;
    }

    public void Resize(int newMaximumEntityCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(newMaximumEntityCount, _maxEntities);

        Array.Resize(ref _entityIdToDenseIndexMap, newMaximumEntityCount);
        for (var i = _maxEntities; i < newMaximumEntityCount; i++)
        {
            _entityIdToDenseIndexMap[i] = -1;
        }

        _maxEntities = newMaximumEntityCount;
    }

    public void Add(int entityId, T newComponent)
    {
        ValidateEntityId(entityId);

        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex >= 0)
        {
            throw new InvalidOperationException($"Entity {entityId} already has a component of type {typeof(T).Name}.");
        }

        EnsureDenseCapacityForOneMore();

        _denseComponents[_count] = newComponent;
        _denseIndexToEntityIdMap[_count] = entityId;
        _entityIdToDenseIndexMap[entityId] = _count;
        _denseVersions[_count] = 1;
        _count++;

        EntityAdded?.Invoke(entityId);
    }

    public void Merge(int entityId, T newComponent)
    {
        ValidateEntityId(entityId);

        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex >= 0)
        {
            _mergeImplementation(ref _denseComponents[denseIndex], newComponent);
            _denseVersions[denseIndex]++;
            return;
        }

        Add(entityId, newComponent);
    }

    public bool Has(int entityId)
    {
        ValidateEntityId(entityId);
        return _entityIdToDenseIndexMap[entityId] >= 0;
    }

    public bool TryGetReadonly(int entityId, out T component)
    {
        ValidateEntityId(entityId);

        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex < 0)
        {
            component = default;
            return false;
        }

        component = _denseComponents[denseIndex];
        return true;
    }

    public ref readonly T GetReadonly(int entityId)
    {
        ValidateEntityId(entityId);

        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex < 0)
        {
            throw new InvalidOperationException($"Entity {entityId} does not have component {typeof(T).Name}.");
        }

        return ref _denseComponents[denseIndex];
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

        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex < 0)
        {
            throw new InvalidOperationException($"Entity {entityId} does not have component {typeof(T).Name}.");
        }

        return _denseVersions[denseIndex];
    }

    public bool TrySet(int entityId, T value)
    {
        ValidateEntityId(entityId);

        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex < 0)
        {
            return false;
        }

        _denseComponents[denseIndex] = value;
        _denseVersions[denseIndex]++;
        return true;
    }

    public bool TryUpdate(int entityId, Engine.ECS.Components.ComponentUpdater<T> updater)
    {
        ValidateEntityId(entityId);
        ArgumentNullException.ThrowIfNull(updater);

        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex < 0)
        {
            return false;
        }

        updater(ref _denseComponents[denseIndex]);
        _denseVersions[denseIndex]++;
        return true;
    }

    public bool TryUpdate<TState>(int entityId, TState state, ComponentUpdater<TState> updater)
    {
        ValidateEntityId(entityId);
        ArgumentNullException.ThrowIfNull(updater);

        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex < 0)
        {
            return false;
        }

        updater(ref _denseComponents[denseIndex], state);
        _denseVersions[denseIndex]++;
        return true;
    }

    public ref T GetByDenseIndex(int denseIndex)
    {
        ValidateDenseIndex(denseIndex);
        return ref _denseComponents[denseIndex];
    }

    public ref readonly T GetReadonlyByDenseIndex(int denseIndex)
    {
        ValidateDenseIndex(denseIndex);
        return ref _denseComponents[denseIndex];
    }

    public int GetEntityIdByDenseIndex(int denseIndex)
    {
        ValidateDenseIndex(denseIndex);
        return _denseIndexToEntityIdMap[denseIndex];
    }

    public uint GetVersionByDenseIndex(int denseIndex)
    {
        ValidateDenseIndex(denseIndex);
        return _denseVersions[denseIndex];
    }

    public void SetByDenseIndex(int denseIndex, T value)
    {
        ValidateDenseIndex(denseIndex);
        _denseComponents[denseIndex] = value;
        _denseVersions[denseIndex]++;
    }

    public void UpdateByDenseIndex(int denseIndex, Engine.ECS.Components.ComponentUpdater<T> updater)
    {
        ValidateDenseIndex(denseIndex);
        ArgumentNullException.ThrowIfNull(updater);

        updater(ref _denseComponents[denseIndex]);
        _denseVersions[denseIndex]++;
    }

    public void UpdateByDenseIndex<TState>(int denseIndex, TState state, ComponentUpdater<TState> updater)
    {
        ValidateDenseIndex(denseIndex);
        ArgumentNullException.ThrowIfNull(updater);

        updater(ref _denseComponents[denseIndex], state);
        _denseVersions[denseIndex]++;
    }

    public void IncrementVersionByDenseIndex(int denseIndex)
    {
        ValidateDenseIndex(denseIndex);
        _denseVersions[denseIndex]++;
    }

    public bool Remove(int entityId)
    {
        ValidateEntityId(entityId);

        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex < 0)
        {
            return false;
        }

        var lastDenseIndex = _count - 1;

        if (denseIndex != lastDenseIndex)
        {
            _denseComponents[denseIndex] = _denseComponents[lastDenseIndex];
            _denseIndexToEntityIdMap[denseIndex] = _denseIndexToEntityIdMap[lastDenseIndex];
            _denseVersions[denseIndex] = _denseVersions[lastDenseIndex];

            var movedEntityId = _denseIndexToEntityIdMap[denseIndex];
            _entityIdToDenseIndexMap[movedEntityId] = denseIndex;
        }

        _entityIdToDenseIndexMap[entityId] = -1;
        _denseComponents[lastDenseIndex] = default;
        _denseIndexToEntityIdMap[lastDenseIndex] = -1;
        _denseVersions[lastDenseIndex] = 0;
        _count--;

        EntityRemoved?.Invoke(entityId);

        return true;
    }

    private void EnsureDenseCapacityForOneMore()
    {
        if (_count < _denseComponents.Length)
        {
            return;
        }

        var newSize = _denseComponents.Length + _denseGrowthAmount;
        Array.Resize(ref _denseComponents, newSize);
        Array.Resize(ref _denseIndexToEntityIdMap, newSize);
        Array.Resize(ref _denseVersions, newSize);

        for (var i = _count; i < newSize; i++)
        {
            _denseIndexToEntityIdMap[i] = -1;
        }
    }

    private void ValidateEntityId(int entityId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(entityId, _maxEntities);
    }

    private void ValidateDenseIndex(int denseIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(denseIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(denseIndex, _count);
    }
}
