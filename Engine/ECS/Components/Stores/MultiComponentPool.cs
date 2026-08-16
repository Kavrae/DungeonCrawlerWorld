using System.Runtime.CompilerServices;

namespace Engine.ECS.Components.Stores;

/// <summary> Packed multi-value component storage. </summary>
/// <remarks> Allows an entity to own 0..N components of the same type while keeping dense global iteration, via an intrusive doubly-linked chain through the dense array per entity. </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class MultiComponentPool<T> : IReadOnlyMultiComponentPool<T>, IInspectableComponentPool, IEntityMembershipPool where T : struct
{
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

    /// <summary> The type of component stored in this pool. </summary>
    public Type ComponentType => typeof(T);

    /// <summary> The number of components in the pool, across every entity. </summary>
    public int Count => _count;

    /// <summary> A read-only span of the components in the pool, packed contiguously by dense index. </summary>
    public ReadOnlySpan<T> Components => new(_denseComponents, 0, _count);

    /// <summary> A read-only span of the entity id owning each component in <see cref="Components"/>, at the same dense index. </summary>
    public ReadOnlySpan<int> EntityIds => new(_denseIndexToEntityIdMap, 0, _count);

    /// <summary> A read-only span of the version for each component in <see cref="Components"/>, at the same dense index. </summary>
    public ReadOnlySpan<uint> Versions => new(_denseVersions, 0, _count);

    /// <summary> A delegate that defines a method for updating a component in place. </summary>
    public delegate void ComponentUpdater(ref T component);

    /// <summary> A delegate that defines a method for updating a component to a given state. </summary>
    public delegate void ComponentUpdater<TState>(ref T component, TState state);

    /// <summary> A delegate that defines a predicate to test against a component. </summary>
    public delegate bool ComponentPredicate(ref readonly T component);

    /// <summary> A delegate that defines a predicate to test against a component, given a state. </summary>
    public delegate bool ComponentPredicate<TState>(ref readonly T component, TState state);

    /// <summary> Fired on an entity's 0-to-1 (EntityAdded) or 1-to-0 (EntityRemoved) membership transition. </summary>
    /// <remarks>
    /// Not fired on every individual Add/Remove call, since an entity can hold several instances
    /// at once here. Mirrors PackedComponentPool's own EntityAdded/EntityRemoved (same purpose:
    /// letting an EntityStripeSet maintain incremental bucket membership), scoped to "does this
    /// entity have any instance at all" rather than "an instance changed," so a system striping
    /// over this pool (e.g. ActionCooldownSystem) still gets exactly one bucket entry per entity
    /// regardless of how many instances that entity carries.
    /// </remarks>
    public event Action<int>? EntityAdded;

    /// <inheritdoc cref="EntityAdded"/>
    public event Action<int>? EntityRemoved;

    /// <summary> Initializes a new instance of the <see cref="MultiComponentPool{T}"/> class with the specified capacities. </summary>
    /// <param name="maximumEntityCount">The maximum EntityId this pool can be indexed by.</param>
    /// <param name="initialCapacity">The initial dense storage size, and the amount it grows by each time it fills.</param>
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

    /// <summary> Resizes the pool to accommodate the new maximum entity count. </summary>
    /// <param name="newMaximumEntityCount">The new maximum entity count.</param>
    public void Resize(int newMaximumEntityCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(newMaximumEntityCount, _maximumEntityCount);

        Array.Resize(ref _entityIdToFirstDenseIndexMap, newMaximumEntityCount);
        Array.Resize(ref _entityCounts, newMaximumEntityCount);
        Array.Resize(ref _entityVersions, newMaximumEntityCount);

        for (var i = _maximumEntityCount; i < newMaximumEntityCount; i++)
        {
            _entityIdToFirstDenseIndexMap[i] = -1;
        }

        _maximumEntityCount = newMaximumEntityCount;
    }

    /// <summary> True if the specified entity has at least one component of this type. </summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    public bool Has(int entityId) => _entityCounts[entityId] > 0;

    /// <summary> Gets how many components of this type the specified entity owns. </summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <returns>The number of components entityId owns.</returns>
    public int CountForEntity(int entityId) => _entityCounts[entityId];

    /// <summary> Gets the entity-scoped version for the specified entity. </summary>
    /// <remarks>Distinct from a single component's own dense-index version: this increments on any Add/Remove/update affecting any of entityId's instances, so a consumer only interested in "did anything about this entity's instances change" doesn't need to track every dense index individually.</remarks>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <returns>The entity-scoped version.</returns>
    public uint GetEntityVersion(int entityId) => _entityVersions[entityId];

    /// <summary> Adds a new component instance to the pool for the specified entity. </summary>
    /// <remarks>Unlike DirectComponentPool/PackedComponentPool, this never merges -- an entity may hold several instances, so there is no single existing one to merge into. The new instance is inserted at the head of entityId's chain.</remarks>
    /// <param name="entityId">The ID of the entity to add the component to.</param>
    /// <param name="component">The component to add.</param>
    public void Add(int entityId, T component)
    {
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

        if (previousFirst == -1)
        {
            EntityAdded?.Invoke(entityId);
        }
    }

    /// <summary> Removes every component instance the specified entity owns. </summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <returns>True if at least one component was removed, false if the entity had none.</returns>
    public bool Remove(int entityId)
    {
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

    /// <summary> Removes the first of entityId's components matching predicate. </summary>
    /// <remarks>Walks the dense per-entity chain in insertion order, same as every other predicate-based lookup on this type -- see TryGetFirst/TryUpdateFirst.</remarks>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="predicate">The predicate a component must match to be removed.</param>
    /// <returns>True if a matching component was found and removed, false otherwise.</returns>
    public bool RemoveFirst(int entityId, ComponentPredicate predicate)
    {
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

    /// <summary> Removes the first of entityId's components matching predicate. </summary>
    /// <remarks>state-passing overload of RemoveFirst -- avoids a per-call closure allocation when predicate needs an external value.</remarks>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="state">The state to pass to the predicate.</param>
    /// <param name="predicate">The predicate a component must match to be removed.</param>
    /// <returns>True if a matching component was found and removed, false otherwise.</returns>
    public bool RemoveFirst<TState>(int entityId, TState state, ComponentPredicate<TState> predicate)
    {
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

    /// <summary> Removes a single component instance by its dense index. </summary>
    /// <remarks>Unconditional -- for callers that already located the instance themselves (e.g. via GetFirstDenseIndex/GetNextDenseIndex) and don't need a predicate re-check.</remarks>
    /// <param name="denseIndex">The dense index of the component instance to remove.</param>
    /// <returns>Always true.</returns>
    public bool RemoveByDenseIndex(int denseIndex)
    {
        RemoveDenseIndexInternal(denseIndex);
        return true;
    }

    /// <summary> Gets the dense index of the first component instance in entityId's chain. </summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <returns>The dense index of the first instance, or -1 if entityId owns none.</returns>
    public int GetFirstDenseIndex(int entityId) => _entityIdToFirstDenseIndexMap[entityId];

    /// <summary> Gets the dense index of the next component instance in the same entity's chain. </summary>
    /// <param name="denseIndex">The dense index to advance from.</param>
    /// <returns>The dense index of the next instance, or -1 if denseIndex was the chain's last.</returns>
    public int GetNextDenseIndex(int denseIndex) => _denseNext[denseIndex];

    /// <summary> Hot-path mutable access to a component instance by its dense index. </summary>
    /// <remarks> WARNING: Caller must manually increment the version (see IncrementVersionByDenseIndex) after mutation. Prefer UpdateByDenseIndex/TryUpdateFirst unless you are in a tight loop. </remarks>
    /// <param name="denseIndex">The dense index of the component instance.</param>
    /// <returns>A mutable reference to the component.</returns>
    public ref T GetByDenseIndex(int denseIndex) => ref _denseComponents[denseIndex];

    /// <summary> Gets a readonly reference to a component instance by its dense index. </summary>
    /// <param name="denseIndex">The dense index of the component instance.</param>
    /// <returns>A readonly reference to the component.</returns>
    public ref readonly T GetReadonlyByDenseIndex(int denseIndex) => ref _denseComponents[denseIndex];

    /// <summary> Gets the owning entity id of a component instance by its dense index. </summary>
    /// <param name="denseIndex">The dense index of the component instance.</param>
    /// <returns>The owning entity's ID.</returns>
    public int GetEntityIdByDenseIndex(int denseIndex) => _denseIndexToEntityIdMap[denseIndex];

    /// <summary> Gets the version of a component instance by its dense index. </summary>
    /// <param name="denseIndex">The dense index of the component instance.</param>
    /// <returns>The version of the component.</returns>
    public uint GetVersionByDenseIndex(int denseIndex) => _denseVersions[denseIndex];

    /// <summary> Returns the string representation of every component instance the specified entity owns. </summary>
    /// <remarks>Equal-value instances (by T's own equality) are grouped into a single entry with an "(xN)" count suffix, rather than one entry per instance -- unlike Direct/PackedComponentPool's single-component listing, an entity here may own many instances and a naive per-instance dump would flood inspection output.</remarks>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="destination">The list to add the inspection data to.</param>
    /// <returns>The number of inspection entries added.</returns>
    public int CopyInspectionDataForEntity(int entityId, List<InspectedComponentEntry> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var groups = new List<(T Value, int Count, uint MaxVersion)>();

        for (var denseIndex = GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = GetNextDenseIndex(denseIndex))
        {
            var value = GetReadonlyByDenseIndex(denseIndex);
            var version = GetVersionByDenseIndex(denseIndex);

            var groupIndex = groups.FindIndex(group => EqualityComparer<T>.Default.Equals(group.Value, value));
            if (groupIndex == -1)
            {
                groups.Add((value, 1, version));
            }
            else
            {
                var existing = groups[groupIndex];
                groups[groupIndex] = (existing.Value, existing.Count + 1, System.Math.Max(existing.MaxVersion, version));
            }
        }

        foreach (var group in groups)
        {
            var text = group.Value.ToString() ?? string.Empty;
            if (group.Count > 1)
            {
                text = $"{text} (x{group.Count})";
            }

            destination.Add(new InspectedComponentEntry(ComponentType, text, group.MaxVersion));
        }

        return groups.Count;
    }

    /// <summary> Increments the version of a component instance, and its owning entity, by dense index. </summary>
    /// <param name="denseIndex">The dense index of the component instance.</param>
    public void IncrementVersionByDenseIndex(int denseIndex)
    {
        _denseVersions[denseIndex]++;

        var entityId = _denseIndexToEntityIdMap[denseIndex];
        _entityVersions[entityId]++;
    }

    /// <summary> Updates a component instance in place by dense index using a custom update function. </summary>
    /// <param name="denseIndex">The dense index of the component instance.</param>
    /// <param name="updater">The function to update the component.</param>
    public void UpdateByDenseIndex(int denseIndex, ComponentUpdater updater)
    {
        ArgumentNullException.ThrowIfNull(updater);

        updater(ref _denseComponents[denseIndex]);
        _denseVersions[denseIndex]++;

        var entityId = _denseIndexToEntityIdMap[denseIndex];
        _entityVersions[entityId]++;
    }

    /// <summary> Updates a component instance in place by dense index using a custom update function and state. </summary>
    /// <typeparam name="TState">The type of the state parameter.</typeparam>
    /// <param name="denseIndex">The dense index of the component instance.</param>
    /// <param name="state">The state to pass to the update function.</param>
    /// <param name="updater">The function to update the component.</param>
    public void UpdateByDenseIndex<TState>(int denseIndex, TState state, ComponentUpdater<TState> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);

        updater(ref _denseComponents[denseIndex], state);
        _denseVersions[denseIndex]++;

        var entityId = _denseIndexToEntityIdMap[denseIndex];
        _entityVersions[entityId]++;
    }

    /// <summary> Finds and updates the first of entityId's components matching predicate. </summary>
    /// <remarks>Walks the dense per-entity chain in insertion order, same as every other predicate-based lookup on this type -- see TryGetFirst/RemoveFirst.</remarks>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="predicate">The predicate a component must match to be updated.</param>
    /// <param name="updater">The function to update the matching component.</param>
    /// <returns>True if a matching component was found and updated, false otherwise.</returns>
    public bool TryUpdateFirst(int entityId, ComponentPredicate predicate, ComponentUpdater updater)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(updater);

        for (var denseIndex = _entityIdToFirstDenseIndexMap[entityId]; denseIndex != -1; denseIndex = _denseNext[denseIndex])
        {
            ref var component = ref _denseComponents[denseIndex];
            if (predicate(ref component))
            {
                updater(ref component);
                _denseVersions[denseIndex]++;
                _entityVersions[entityId]++;
                return true;
            }
        }

        return false;
    }

    /// <summary> Finds and updates the first of entityId's components matching predicate. </summary>
    /// <remarks>state-passing overload of TryUpdateFirst -- avoids a per-call closure allocation when predicate/updater need an external value.</remarks>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="state">The state to pass to the predicate and update function.</param>
    /// <param name="predicate">The predicate a component must match to be updated.</param>
    /// <param name="updater">The function to update the matching component.</param>
    /// <returns>True if a matching component was found and updated, false otherwise.</returns>
    public bool TryUpdateFirst<TState>(
        int entityId,
        TState state,
        ComponentPredicate<TState> predicate,
        ComponentUpdater<TState> updater)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(updater);

        for (var denseIndex = _entityIdToFirstDenseIndexMap[entityId]; denseIndex != -1; denseIndex = _denseNext[denseIndex])
        {
            ref var component = ref _denseComponents[denseIndex];
            if (predicate(ref component, state))
            {
                updater(ref component, state);
                _denseVersions[denseIndex]++;
                _entityVersions[entityId]++;
                return true;
            }
        }

        return false;
    }

    /// <summary>Finds the first component matching predicate for entityId.</summary>
    /// <remarks>Walks the dense per-entity chain in insertion order, same as every other predicate-based lookup on this type -- see TryUpdateFirst/RemoveFirst.</remarks>
    public bool TryGetFirst(int entityId, ComponentPredicate predicate, out T result)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        for (var denseIndex = _entityIdToFirstDenseIndexMap[entityId]; denseIndex != -1; denseIndex = _denseNext[denseIndex])
        {
            ref readonly var component = ref _denseComponents[denseIndex];
            if (predicate(in component))
            {
                result = component;
                return true;
            }
        }

        result = default!;
        return false;
    }

    /// <summary>Finds the first component matching predicate for entityId.</summary>
    /// <remarks>state-passing overload of TryGetFirst -- avoids a per-call closure allocation when predicate needs an external value, the same reasoning TryUpdateFirst/RemoveFirst's own TState overloads use.</remarks>
    public bool TryGetFirst<TState>(int entityId, TState state, ComponentPredicate<TState> predicate, out T result)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        for (var denseIndex = _entityIdToFirstDenseIndexMap[entityId]; denseIndex != -1; denseIndex = _denseNext[denseIndex])
        {
            ref readonly var component = ref _denseComponents[denseIndex];
            if (predicate(in component, state))
            {
                result = component;
                return true;
            }
        }

        result = default!;
        return false;
    }

    /// <summary>Counts how many of entityId's components match predicate.</summary>
    /// <remarks>Named CountMatching, not Count, so it doesn't collide with the whole-pool Count property or the predicate-less per-entity CountForEntity.</remarks>
    public int CountMatching(int entityId, ComponentPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var matchCount = 0;
        for (var denseIndex = _entityIdToFirstDenseIndexMap[entityId]; denseIndex != -1; denseIndex = _denseNext[denseIndex])
        {
            if (predicate(ref _denseComponents[denseIndex]))
            {
                matchCount++;
            }
        }

        return matchCount;
    }

    /// <summary>Counts how many of entityId's components match predicate.</summary>
    /// <remarks>state-passing overload of CountMatching -- same closure-avoidance reasoning as TryGetFirst's own TState overload.</remarks>
    public int CountMatching<TState>(int entityId, TState state, ComponentPredicate<TState> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var matchCount = 0;
        for (var denseIndex = _entityIdToFirstDenseIndexMap[entityId]; denseIndex != -1; denseIndex = _denseNext[denseIndex])
        {
            if (predicate(ref _denseComponents[denseIndex], state))
            {
                matchCount++;
            }
        }

        return matchCount;
    }

    /// <summary>Copies every component instance entityId owns into destination.</summary>
    /// <remarks>Clears destination first -- callers reuse one destination list call over call rather than allocating a fresh one.</remarks>
    public void CopyAll(int entityId, List<T> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        destination.Clear();
        for (var denseIndex = _entityIdToFirstDenseIndexMap[entityId]; denseIndex != -1; denseIndex = _denseNext[denseIndex])
        {
            destination.Add(_denseComponents[denseIndex]);
        }
    }

    /// <summary>Clears all components from the pool.</summary>
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

    /// <summary> Unlinks a component instance from its owner's chain and swap-removes it from dense storage. </summary>
    /// <remarks>Swaps the last dense slot into the freed one (so dense storage stays contiguous) and re-patches whichever chain the moved entry belonged to, mirroring PackedComponentPool.Remove's own swap-with-last approach but with an extra chain-relink step for the doubly-linked list.</remarks>
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

        if (_entityCounts[ownerEntityId] == 0)
        {
            EntityRemoved?.Invoke(ownerEntityId);
        }

        var lastDenseIndex = _count - 1;

        if (denseIndex != lastDenseIndex)
        {
            MoveDenseEntry(lastDenseIndex, denseIndex);
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _denseComponents[lastDenseIndex] = default;
        }

        _denseIndexToEntityIdMap[lastDenseIndex] = -1;
        _denseVersions[lastDenseIndex] = 0;
        _denseNext[lastDenseIndex] = -1;
        _densePrevious[lastDenseIndex] = -1;

        _count--;
    }

    /// <summary> Relocates a dense entry from one index to another, re-patching its owner's chain to point at the new location. </summary>
    /// <remarks>Used by RemoveDenseIndexInternal to move the last dense slot into a freed one -- the entry's own next/previous links move with it unchanged, only the chain's pointers into it (its owner's head pointer, or its neighbors' next/previous) are repointed.</remarks>
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

    /// <summary> Grows dense storage by <c>_denseGrowthAmount</c> if it's currently full. </summary>
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
            _denseNext[i] = -1;
            _densePrevious[i] = -1;
        }
    }
}
