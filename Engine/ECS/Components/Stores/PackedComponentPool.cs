using System.Runtime.CompilerServices;

namespace Engine.ECS.Components.Stores;

/// <summary> Sparse-set component storage for rare components, where a direct pool would waste index space. </summary>
/// <remarks> Dense storage grows linearly to bound peak memory for components most entities never have. </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class PackedComponentPool<T> : IReadOnlyComponentPool<T>, IInspectableComponentPool, IEntityMembershipPool where T : struct
{
    private int _maxEntities;
    private int[] _entityIdToDenseIndexMap;
    private int[] _denseIndexToEntityIdMap;
    private T[] _denseComponents;
    private uint[] _denseVersions;
    private readonly int _denseGrowthAmount;
    private readonly MergeAction<T> _mergeImplementation;

    private int _count;

    /// <summary> The type of component stored in this pool. </summary>
    public Type ComponentType => typeof(T);


    /// <summary> The number of components in the pool. </summary>
    public int Count => _count;

    /// <summary> A read-only span of the components in the pool, packed contiguously by dense index. </summary>
    public ReadOnlySpan<T> Components => new(_denseComponents, 0, _count);

    /// <summary> A read-only span of the entity id owning each component in <see cref="Components"/>, at the same dense index. </summary>
    public ReadOnlySpan<int> EntityIds => new(_denseIndexToEntityIdMap, 0, _count);

    /// <summary> A read-only span of the version for each component in <see cref="Components"/>, at the same dense index. </summary>
    public ReadOnlySpan<uint> Versions => new(_denseVersions, 0, _count);

    /// <summary> A delegate that defines a method for updating a component to a given state. </summary>
    public delegate void ComponentUpdater<TState>(ref T component, TState state);

    /// <summary> Fired at the end of Add (including Merge's fallback-to-Add path) and Remove. </summary>
    /// <remarks>
    /// Lets consumers (e.g. EntityStripeSet) maintain an entityId-keyed view of this pool's
    /// membership that stays correct across Remove's swap-with-last dense-index reshuffling,
    /// instead of re-deriving membership from live dense indices, which are not stable
    /// identifiers for an entity across time under churn.
    /// </remarks>
    public event Action<int>? EntityAdded;

    /// <inheritdoc cref="EntityAdded"/>
    public event Action<int>? EntityRemoved;

    /// <summary> Initializes a new instance of the <see cref="PackedComponentPool{T}"/> class with the specified capacities and merge implementation. </summary>
    /// <param name="maximumEntityCount">The maximum EntityId this pool can be indexed by.</param>
    /// <param name="initialCapacity">The initial dense storage size, and the amount it grows by each time it fills.</param>
    /// <param name="mergeImplementation">Determines how two instances of a component should be merged together.</param>
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

    /// <summary> Resizes the pool to accommodate the new maximum entity count. </summary>
    /// <param name="newMaximumEntityCount">The new maximum entity count.</param>
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

    /// <summary> Adds a component to the pool for the specified entity. </summary>
    /// <param name="entityId">The ID of the entity to add the component to.</param>
    /// <param name="newComponent">The component to add.</param>
    public void Add(int entityId, T newComponent)
    {
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

    /// <summary> Merges a component with the existing component for the specified entity. </summary>
    /// <remarks>If the entity does not have a component of this type, it will be added instead.
    /// The merge implementation defines how this component should handle each property.</remarks>
    /// <param name="entityId">The ID of the entity to merge the component with.</param>
    /// <param name="newComponent">The component to merge.</param>
    public void Merge(int entityId, T newComponent)
    {
        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex >= 0)
        {
            _mergeImplementation(ref _denseComponents[denseIndex], newComponent);
            _denseVersions[denseIndex]++;
            return;
        }

        Add(entityId, newComponent);
    }

    /// <summary>True if the specified entity has a component of this type</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    public bool Has(int entityId) => _entityIdToDenseIndexMap[entityId] >= 0;

    /// <summary>Attempts to get a readonly reference to the component for the specified entity.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="component">Stores a readonly reference to the component if the entity has one, or the default value if not.</param>
    public bool TryGetReadonly(int entityId, out T component)
    {
        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex < 0)
        {
            component = default;
            return false;
        }

        component = _denseComponents[denseIndex];
        return true;
    }

    /// <summary>Gets a readonly reference to the component for the specified entity.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    public ref readonly T GetReadonly(int entityId)
    {
        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex < 0)
        {
            throw new InvalidOperationException($"Entity {entityId} does not have component {typeof(T).Name}.");
        }

        return ref _denseComponents[denseIndex];
    }

    /// <summary>Returns the string representation of the component for the specified entity.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="destination">The list to add the inspection data to.</param>
    /// <returns>The number of inspection entries added.</returns>
    public int CopyInspectionDataForEntity(int entityId, List<InspectedComponentEntry> destination)
    {
        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex < 0)
        {
            return 0;
        }

        destination.Add(new InspectedComponentEntry(
            ComponentType,
            _denseComponents[denseIndex].ToString() ?? string.Empty,
            _denseVersions[denseIndex]));

        return 1;
    }

    /// <summary>Gets the version of the component for the specified entity.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <returns>The version of the component.</returns>
    public uint GetVersion(int entityId)
    {
        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex < 0)
        {
            throw new InvalidOperationException($"Entity {entityId} does not have component {typeof(T).Name}.");
        }

        return _denseVersions[denseIndex];
    }

    /// <summary>Attempts to set the component for the specified entity if it already has one.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>True if the component was set, false otherwise.</returns>
    public bool TrySet(int entityId, T value)
    {
        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex < 0)
        {
            return false;
        }

        _denseComponents[denseIndex] = value;
        _denseVersions[denseIndex]++;
        return true;
    }

    /// <summary>Attempts to update the component for the specified entity using a custom update function if it already has one.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="updater">The function to update the component.</param>
    /// <returns>True if the component was updated, false otherwise.</returns>
    public bool TryUpdate(int entityId, Engine.ECS.Components.ComponentUpdater<T> updater)
    {
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

    /// <summary>Attempts to update the component for the specified entity using a custom update function and state if it already has one.</summary>
    /// <typeparam name="TState">The type of the state parameter.</typeparam>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="state">The state to pass to the update function.</param>
    /// <param name="updater">The function to update the component.</param>
    /// <returns>True if the component was updated, false otherwise.</returns>
    public bool TryUpdate<TState>(int entityId, TState state, ComponentUpdater<TState> updater)
    {
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

    /// <summary> Hot-path mutable access to the component by its dense index. </summary>
    /// <remarks> WARNING: Caller must manually increment version (see IncrementVersionByDenseIndex) after mutation. Prefer UpdateByDenseIndex/TryUpdate unless you are in a tight loop. </remarks>
    /// <param name="denseIndex">The dense index of the component.</param>
    /// <returns>A mutable reference to the component.</returns>
    public ref T GetByDenseIndex(int denseIndex) => ref _denseComponents[denseIndex];

    /// <summary> Gets a readonly reference to the component by its dense index. </summary>
    /// <param name="denseIndex">The dense index of the component.</param>
    /// <returns>A readonly reference to the component.</returns>
    public ref readonly T GetReadonlyByDenseIndex(int denseIndex) => ref _denseComponents[denseIndex];

    /// <summary> Gets the owning entity id of the component at the given dense index. </summary>
    /// <param name="denseIndex">The dense index of the component.</param>
    /// <returns>The owning entity's ID.</returns>
    public int GetEntityIdByDenseIndex(int denseIndex) => _denseIndexToEntityIdMap[denseIndex];

    /// <summary> Gets the version of the component at the given dense index. </summary>
    /// <param name="denseIndex">The dense index of the component.</param>
    /// <returns>The version of the component.</returns>
    public uint GetVersionByDenseIndex(int denseIndex) => _denseVersions[denseIndex];

    /// <summary> Sets the component at the given dense index. </summary>
    /// <param name="denseIndex">The dense index of the component.</param>
    /// <param name="value">The value to set.</param>
    public void SetByDenseIndex(int denseIndex, T value)
    {
        _denseComponents[denseIndex] = value;
        _denseVersions[denseIndex]++;
    }

    /// <summary> Updates the component at the given dense index using a custom update function. </summary>
    /// <param name="denseIndex">The dense index of the component.</param>
    /// <param name="updater">The function to update the component.</param>
    public void UpdateByDenseIndex(int denseIndex, Engine.ECS.Components.ComponentUpdater<T> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);

        updater(ref _denseComponents[denseIndex]);
        _denseVersions[denseIndex]++;
    }

    /// <summary> Updates the component at the given dense index using a custom update function and state. </summary>
    /// <typeparam name="TState">The type of the state parameter.</typeparam>
    /// <param name="denseIndex">The dense index of the component.</param>
    /// <param name="state">The state to pass to the update function.</param>
    /// <param name="updater">The function to update the component.</param>
    public void UpdateByDenseIndex<TState>(int denseIndex, TState state, ComponentUpdater<TState> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);

        updater(ref _denseComponents[denseIndex], state);
        _denseVersions[denseIndex]++;
    }

    /// <summary> Increments the version of the component at the given dense index. </summary>
    /// <param name="denseIndex">The dense index of the component.</param>
    public void IncrementVersionByDenseIndex(int denseIndex) => _denseVersions[denseIndex]++;

    /// <summary>Removes the component for the specified entity if it exists.</summary>
    /// <remarks>Swaps the last dense slot into the freed one to keep dense storage contiguous, then re-patches the moved entity's own index mapping -- see MultiComponentPool.RemoveDenseIndexInternal for the same approach with an added linked-chain relink step.</remarks>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <returns>True if the component was removed, false otherwise.</returns>
    public bool Remove(int entityId)
    {
        var denseIndex = _entityIdToDenseIndexMap[entityId];
        if (denseIndex < 0)
        {
            return false;
        }

        var lastDenseIndex = _count - 1;

        if (denseIndex != lastDenseIndex)
        {
            var movedEntityId = _denseIndexToEntityIdMap[lastDenseIndex];

            _denseComponents[denseIndex] = _denseComponents[lastDenseIndex];
            _denseIndexToEntityIdMap[denseIndex] = movedEntityId;
            _denseVersions[denseIndex] = _denseVersions[lastDenseIndex];

            _entityIdToDenseIndexMap[movedEntityId] = denseIndex;
        }

        _entityIdToDenseIndexMap[entityId] = -1;

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _denseComponents[lastDenseIndex] = default;
        }

        _denseIndexToEntityIdMap[lastDenseIndex] = -1;
        _denseVersions[lastDenseIndex] = 0;
        _count--;

        EntityRemoved?.Invoke(entityId);

        return true;
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

        for (var i = _count; i < newSize; i++)
        {
            _denseIndexToEntityIdMap[i] = -1;
        }
    }
}
