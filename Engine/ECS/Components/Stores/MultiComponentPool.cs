namespace Engine.ECS.Components.Stores;

/// <summary>
/// Packed multi-value component storage. Allows an entity to own 0..N components of the
/// same type while keeping dense global iteration, via an intrusive doubly-linked chain
/// through the dense array per entity. Best for cases like Race/Class where an entity can
/// legitimately have several.
/// </summary>
public sealed class MultiComponentPool<T> : IReadOnlyMultiComponentPool<T>, IInspectableComponentPool where T : struct
{
    public Type ComponentType => typeof(T);
    public ComponentPoolType ComponentPoolType => ComponentPoolType.Multi;

    private int _maximumEntityCount;

    // Dense packed storage
    private T[] _denseComponents;
    private int[] _denseIndexToEntityIdMap;
    private uint[] _denseVersions;

    // Per-entity linked chains into dense storage
    private int[] _entityIdToFirstDenseIndexMap;
    private int[] _denseNext;
    private int[] _densePrevious;

    // Per-entity metadata
    private int[] _entityCounts;
    private uint[] _entityVersions;

    private readonly int _denseGrowthAmount;
    private int _count;

    public int Count => _count;

    public ReadOnlySpan<T> Components => new(_denseComponents, 0, _count);
    public ReadOnlySpan<int> EntityIds => new(_denseIndexToEntityIdMap, 0, _count);
    public ReadOnlySpan<uint> Versions => new(_denseVersions, 0, _count);

    public delegate void ComponentUpdater(ref T component);
    public delegate void ComponentUpdater<TState>(ref T component, TState state);
    public delegate bool ComponentPredicate(ref readonly T component);
    public delegate bool ComponentPredicate<TState>(ref readonly T component, TState state);

    public MultiComponentPool(int maximumEntityCount, int initialCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);

        _maximumEntityCount = maximumEntityCount;

        _entityIdToFirstDenseIndexMap = new int[_maximumEntityCount];
        _entityCounts = new int[_maximumEntityCount];
        _entityVersions = new uint[_maximumEntityCount];
        Array.Fill(_entityIdToFirstDenseIndexMap, -1);

        _denseComponents = new T[initialCapacity];
        _denseIndexToEntityIdMap = new int[initialCapacity];
        _denseVersions = new uint[initialCapacity];
        _denseNext = new int[initialCapacity];
        _densePrevious = new int[initialCapacity];

        Array.Fill(_denseIndexToEntityIdMap, -1);
        Array.Fill(_denseNext, -1);
        Array.Fill(_densePrevious, -1);

        _denseGrowthAmount = initialCapacity;
        _count = 0;
    }

    public void Resize(int newMaximumEntityCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(newMaximumEntityCount, _maximumEntityCount);

        Array.Resize(ref _entityIdToFirstDenseIndexMap, newMaximumEntityCount);
        Array.Resize(ref _entityCounts, newMaximumEntityCount);
        Array.Resize(ref _entityVersions, newMaximumEntityCount);

        for (var i = _maximumEntityCount; i < newMaximumEntityCount; i++)
        {
            _entityIdToFirstDenseIndexMap[i] = -1;
            _entityCounts[i] = 0;
            _entityVersions[i] = 0;
        }

        _maximumEntityCount = newMaximumEntityCount;
    }

    public bool Has(int entityId)
    {
        ValidateEntityId(entityId);
        return _entityCounts[entityId] > 0;
    }

    public int CountForEntity(int entityId)
    {
        ValidateEntityId(entityId);
        return _entityCounts[entityId];
    }

    public uint GetEntityVersion(int entityId)
    {
        ValidateEntityId(entityId);
        return _entityVersions[entityId];
    }

    public void Add(int entityId, T component)
    {
        ValidateEntityId(entityId);
        EnsureDenseCapacityForOneMore();

        var newDenseIndex = _count++;
        var previousFirst = _entityIdToFirstDenseIndexMap[entityId];

        _denseComponents[newDenseIndex] = component;
        _denseIndexToEntityIdMap[newDenseIndex] = entityId;
        _denseVersions[newDenseIndex] = 1;

        _denseNext[newDenseIndex] = previousFirst;
        _densePrevious[newDenseIndex] = -1;

        if (previousFirst != -1)
        {
            _densePrevious[previousFirst] = newDenseIndex;
        }

        _entityIdToFirstDenseIndexMap[entityId] = newDenseIndex;
        _entityCounts[entityId]++;
        _entityVersions[entityId]++;
    }

    public bool Remove(int entityId)
    {
        ValidateEntityId(entityId);

        var denseIndex = _entityIdToFirstDenseIndexMap[entityId];
        if (denseIndex == -1)
        {
            return false;
        }

        while (_entityIdToFirstDenseIndexMap[entityId] != -1)
        {
            RemoveDenseIndexInternal(_entityIdToFirstDenseIndexMap[entityId]);
        }

        return true;
    }

    public bool RemoveFirst(int entityId, ComponentPredicate predicate)
    {
        ValidateEntityId(entityId);
        ArgumentNullException.ThrowIfNull(predicate);

        for (var denseIndex = _entityIdToFirstDenseIndexMap[entityId]; denseIndex != -1;)
        {
            var next = _denseNext[denseIndex];

            if (predicate(ref _denseComponents[denseIndex]))
            {
                RemoveDenseIndexInternal(denseIndex);
                return true;
            }

            denseIndex = next;
        }

        return false;
    }

    public bool RemoveFirst<TState>(int entityId, TState state, ComponentPredicate<TState> predicate)
    {
        ValidateEntityId(entityId);
        ArgumentNullException.ThrowIfNull(predicate);

        for (var denseIndex = _entityIdToFirstDenseIndexMap[entityId]; denseIndex != -1;)
        {
            var next = _denseNext[denseIndex];

            if (predicate(ref _denseComponents[denseIndex], state))
            {
                RemoveDenseIndexInternal(denseIndex);
                return true;
            }

            denseIndex = next;
        }

        return false;
    }

    public bool RemoveByDenseIndex(int denseIndex)
    {
        ValidateDenseIndex(denseIndex);
        RemoveDenseIndexInternal(denseIndex);
        return true;
    }

    public int GetFirstDenseIndex(int entityId)
    {
        ValidateEntityId(entityId);
        return _entityIdToFirstDenseIndexMap[entityId];
    }

    public int GetNextDenseIndex(int denseIndex)
    {
        ValidateDenseIndex(denseIndex);
        return _denseNext[denseIndex];
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

    public int CopyInspectionDataForEntity(int entityId, List<InspectedComponentEntry> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateEntityId(entityId);

        var componentCount = 0;

        for (var denseIndex = GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = GetNextDenseIndex(denseIndex))
        {
            destination.Add(new InspectedComponentEntry(
                ComponentType,
                ComponentPoolType,
                GetReadonlyByDenseIndex(denseIndex),
                GetVersionByDenseIndex(denseIndex)));

            componentCount++;
        }

        return componentCount;
    }

    public void IncrementVersionByDenseIndex(int denseIndex)
    {
        ValidateDenseIndex(denseIndex);
        _denseVersions[denseIndex]++;

        var entityId = _denseIndexToEntityIdMap[denseIndex];
        _entityVersions[entityId]++;
    }

    public void UpdateByDenseIndex(int denseIndex, ComponentUpdater updater)
    {
        ValidateDenseIndex(denseIndex);
        ArgumentNullException.ThrowIfNull(updater);

        updater(ref _denseComponents[denseIndex]);
        _denseVersions[denseIndex]++;

        var entityId = _denseIndexToEntityIdMap[denseIndex];
        _entityVersions[entityId]++;
    }

    public void UpdateByDenseIndex<TState>(int denseIndex, TState state, ComponentUpdater<TState> updater)
    {
        ValidateDenseIndex(denseIndex);
        ArgumentNullException.ThrowIfNull(updater);

        updater(ref _denseComponents[denseIndex], state);
        _denseVersions[denseIndex]++;

        var entityId = _denseIndexToEntityIdMap[denseIndex];
        _entityVersions[entityId]++;
    }

    public bool TryUpdateFirst(int entityId, ComponentPredicate predicate, ComponentUpdater updater)
    {
        ValidateEntityId(entityId);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(updater);

        for (var denseIndex = _entityIdToFirstDenseIndexMap[entityId]; denseIndex != -1; denseIndex = _denseNext[denseIndex])
        {
            if (predicate(ref _denseComponents[denseIndex]))
            {
                updater(ref _denseComponents[denseIndex]);
                _denseVersions[denseIndex]++;
                _entityVersions[entityId]++;
                return true;
            }
        }

        return false;
    }

    public bool TryUpdateFirst<TState>(
        int entityId,
        TState state,
        ComponentPredicate<TState> predicate,
        ComponentUpdater<TState> updater)
    {
        ValidateEntityId(entityId);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(updater);

        for (var denseIndex = _entityIdToFirstDenseIndexMap[entityId]; denseIndex != -1; denseIndex = _denseNext[denseIndex])
        {
            if (predicate(ref _denseComponents[denseIndex], state))
            {
                updater(ref _denseComponents[denseIndex], state);
                _denseVersions[denseIndex]++;
                _entityVersions[entityId]++;
                return true;
            }
        }

        return false;
    }

    public void Clear()
    {
        Array.Clear(_denseComponents, 0, _count);
        Array.Fill(_denseIndexToEntityIdMap, -1, 0, _count);
        Array.Fill(_denseVersions, (uint)0, 0, _count);
        Array.Fill(_denseNext, -1, 0, _count);
        Array.Fill(_densePrevious, -1, 0, _count);

        Array.Fill(_entityIdToFirstDenseIndexMap, -1);
        Array.Clear(_entityCounts);
        Array.Clear(_entityVersions);

        _count = 0;
    }

    private void RemoveDenseIndexInternal(int denseIndex)
    {
        var ownerEntityId = _denseIndexToEntityIdMap[denseIndex];
        var prev = _densePrevious[denseIndex];
        var next = _denseNext[denseIndex];

        if (prev == -1)
        {
            _entityIdToFirstDenseIndexMap[ownerEntityId] = next;
        }
        else
        {
            _denseNext[prev] = next;
        }

        if (next != -1)
        {
            _densePrevious[next] = prev;
        }

        _entityCounts[ownerEntityId]--;
        _entityVersions[ownerEntityId]++;

        var lastDenseIndex = _count - 1;

        if (denseIndex != lastDenseIndex)
        {
            MoveDenseEntry(lastDenseIndex, denseIndex);
        }

        _denseComponents[lastDenseIndex] = default;
        _denseIndexToEntityIdMap[lastDenseIndex] = -1;
        _denseVersions[lastDenseIndex] = 0;
        _denseNext[lastDenseIndex] = -1;
        _densePrevious[lastDenseIndex] = -1;

        _count--;
    }

    private void MoveDenseEntry(int fromDenseIndex, int toDenseIndex)
    {
        var movedEntityId = _denseIndexToEntityIdMap[fromDenseIndex];
        var movedPrev = _densePrevious[fromDenseIndex];
        var movedNext = _denseNext[fromDenseIndex];

        _denseComponents[toDenseIndex] = _denseComponents[fromDenseIndex];
        _denseIndexToEntityIdMap[toDenseIndex] = movedEntityId;
        _denseVersions[toDenseIndex] = _denseVersions[fromDenseIndex];
        _denseNext[toDenseIndex] = movedNext;
        _densePrevious[toDenseIndex] = movedPrev;

        if (movedPrev == -1)
        {
            _entityIdToFirstDenseIndexMap[movedEntityId] = toDenseIndex;
        }
        else
        {
            _denseNext[movedPrev] = toDenseIndex;
        }

        if (movedNext != -1)
        {
            _densePrevious[movedNext] = toDenseIndex;
        }
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
        Array.Resize(ref _denseNext, newSize);
        Array.Resize(ref _densePrevious, newSize);

        for (var i = _count; i < newSize; i++)
        {
            _denseIndexToEntityIdMap[i] = -1;
            _denseVersions[i] = 0;
            _denseNext[i] = -1;
            _densePrevious[i] = -1;
        }
    }

    private void ValidateEntityId(int entityId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(entityId, _maximumEntityCount);
    }

    private void ValidateDenseIndex(int denseIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(denseIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(denseIndex, _count);
    }
}
