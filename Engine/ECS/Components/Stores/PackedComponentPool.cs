using System.Runtime.CompilerServices;

namespace Engine.ECS.Components.Stores;

/// <summary> Sparse-set component storage for rare components, where a direct pool would waste index space. </summary>
/// <remarks> The entity index is paged (see EntityPages), so a pool costs the id ranges its holders fall in rather than the world's entity capacity; dense storage grows geometrically (see DenseCapacityGrowth). </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class PackedComponentPool<T> : IReadOnlyComponentPool<T>, IInspectableComponentPool, IEntityMembershipPool, IMemoryReportingComponentPool where T : struct
{
    private readonly EntityPages<int> _denseIndexByEntity;
    private int[] _denseIndexToEntityIdMap;
    private T[] _denseComponents;
    private uint[] _denseVersions;
    private readonly MergeAction<T> _mergeImplementation;

    private int _count;

    /// <summary>Held by the single timer wheel driving this pool, if any -- see TimerWheelClaim for why there can only be one.</summary>
    private TimerWheelClaim _timerWheelClaim;

    /// <summary> The type of component stored in this pool. </summary>
    public Type ComponentType => typeof(T);

    /// <inheritdoc/>
    public IEntityAccessGuard? AccessGuard { get; set; }

    [System.Diagnostics.Conditional("DEBUG")]
    private void Guard(int entityId) => AccessGuard?.OnAccess(typeof(T), entityId);

    private int DenseIndexOf(int entityId)
    {
        Guard(entityId);
        return _denseIndexByEntity.Get(entityId);
    }


    /// <summary> The number of components in the pool. </summary>
    public int Count => _count;

    /// <summary> Estimated bytes across the entity index's allocated pages plus the dense _denseComponents/_denseIndexToEntityIdMap/_denseVersions arrays. </summary>
    public long EstimatedBytes =>
        _denseIndexByEntity.EstimatedBytes +
        (long)_denseComponents.Length * (Unsafe.SizeOf<T>() + sizeof(int) + sizeof(uint));

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

    /// <summary>
    /// Opt-in: fired after every write to a component -- (entityId, denseIndex) -- at exactly the
    /// points the pool bumps that component's version: Add, Merge, TrySet, TryUpdate,
    /// SetByDenseIndex, UpdateByDenseIndex and IncrementVersionByDenseIndex. Not fired by Remove.
    /// </summary>
    /// <remarks>
    /// Lets a consumer react to *every* change without each writer having to remember to tell it
    /// -- the TimerWheel uses this to schedule a ticking countdown whenever its deadline is written,
    /// wherever that write happens. A pool nobody observes pays one
    /// null check per write. The one write this can't see is a raw GetByDenseIndex ref mutation
    /// without the IncrementVersionByDenseIndex that method's contract already requires.
    /// The handler must not add to or remove from this pool.
    /// </remarks>
    public event Action<int, int>? ComponentChanged;

    /// <summary>Opt-in: fired before a component is removed -- (entityId, denseIndex) -- while it can still be read.</summary>
    /// <remarks>Raised by Remove, including RemoveAllComponents'. A handler may read the component and write other pools, but must not add to or remove from this one.</remarks>
    public event Action<int, int>? ComponentRemoving;

    /// <summary> Initializes a new instance of the <see cref="PackedComponentPool{T}"/> class with the specified capacities and merge implementation. </summary>
    /// <param name="entityCapacity">The entity id space the entity index's page table starts out covering; ids beyond it grow it on demand.</param>
    /// <param name="initialCapacity">The initial dense storage size.</param>
    /// <param name="mergeImplementation">Determines how two instances of a component should be merged together.</param>
    public PackedComponentPool(int entityCapacity, int initialCapacity, MergeAction<T> mergeImplementation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entityCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        ArgumentNullException.ThrowIfNull(mergeImplementation);

        _denseIndexByEntity = new EntityPages<int>(entityCapacity, empty: -1);

        _denseComponents = new T[initialCapacity];
        _denseVersions = new uint[initialCapacity];
        _denseIndexToEntityIdMap = new int[initialCapacity];
        Array.Fill(_denseIndexToEntityIdMap, -1);

        _mergeImplementation = mergeImplementation;
        _count = 0;
    }

    /// <summary> Grows the entity index's page table to cover the new entity capacity. Allocates no pages. </summary>
    /// <param name="newMaximumEntityCount">The new entity capacity.</param>
    public void Resize(int newMaximumEntityCount) => _denseIndexByEntity.EnsureCapacity(newMaximumEntityCount);

    /// <summary>Claims this pool as the source for one PackedTimerWheel, throwing if a wheel already drives it.</summary>
    /// <remarks>Called by the wheel's constructor. See TimerWheelClaim.</remarks>
    internal void ClaimForTimerWheel() => _timerWheelClaim.Claim(typeof(T));

    /// <summary> Adds a component to the pool for the specified entity. </summary>
    /// <param name="entityId">The ID of the entity to add the component to.</param>
    /// <param name="newComponent">The component to add.</param>
    public void Add(int entityId, T newComponent)
    {
        Guard(entityId);
        ref var denseIndex = ref _denseIndexByEntity.GetWritable(entityId);
        if (denseIndex >= 0)
        {
            throw new InvalidOperationException($"Entity {entityId} already has a component of type {typeof(T).Name}.");
        }

        EnsureDenseCapacityForOneMore();

        _denseComponents[_count] = newComponent;
        _denseIndexToEntityIdMap[_count] = entityId;
        denseIndex = _count;
        _denseVersions[_count] = 1;
        _count++;

        EntityAdded?.Invoke(entityId);
        ComponentChanged?.Invoke(entityId, _count - 1);
    }

    /// <summary> Merges a component with the existing component for the specified entity. </summary>
    /// <remarks>If the entity does not have a component of this type, it will be added instead.
    /// The merge implementation defines how this component should handle each property.</remarks>
    /// <param name="entityId">The ID of the entity to merge the component with.</param>
    /// <param name="newComponent">The component to merge.</param>
    public void Merge(int entityId, T newComponent)
    {
        var denseIndex = DenseIndexOf(entityId);
        if (denseIndex >= 0)
        {
            _mergeImplementation(ref _denseComponents[denseIndex], newComponent);
            MarkChanged(denseIndex);
            return;
        }

        Add(entityId, newComponent);
    }

    /// <summary>True if the specified entity has a component of this type</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    public bool Has(int entityId) => DenseIndexOf(entityId) >= 0;

    /// <summary>Attempts to get a readonly reference to the component for the specified entity.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="component">Stores a readonly reference to the component if the entity has one, or the default value if not.</param>
    public bool TryGetReadonly(int entityId, out T component)
    {
        var denseIndex = DenseIndexOf(entityId);
        if (denseIndex < 0)
        {
            component = default;
            return false;
        }

        component = _denseComponents[denseIndex];
        return true;
    }

    /// <summary>The entity's current dense index, or -1 if it has no component here.</summary>
    /// <remarks>Valid only until the next Remove from this pool (swap-with-last reshuffles dense indices) -- look it up, use it, drop it.</remarks>
    public int GetDenseIndex(int entityId) => DenseIndexOf(entityId);

    /// <summary>Gets a readonly reference to the component for the specified entity.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    public ref readonly T GetReadonly(int entityId)
    {
        var denseIndex = DenseIndexOf(entityId);
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
        var denseIndex = DenseIndexOf(entityId);
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
        var denseIndex = DenseIndexOf(entityId);
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
        var denseIndex = DenseIndexOf(entityId);
        if (denseIndex < 0)
        {
            return false;
        }

        _denseComponents[denseIndex] = value;
        MarkChanged(denseIndex);
        return true;
    }

    /// <summary>Attempts to update the component for the specified entity using a custom update function if it already has one.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="updater">The function to update the component.</param>
    /// <returns>True if the component was updated, false otherwise.</returns>
    public bool TryUpdate(int entityId, Engine.ECS.Components.ComponentUpdater<T> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);

        var denseIndex = DenseIndexOf(entityId);
        if (denseIndex < 0)
        {
            return false;
        }

        updater(ref _denseComponents[denseIndex]);
        MarkChanged(denseIndex);
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

        var denseIndex = DenseIndexOf(entityId);
        if (denseIndex < 0)
        {
            return false;
        }

        updater(ref _denseComponents[denseIndex], state);
        MarkChanged(denseIndex);
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
        MarkChanged(denseIndex);
    }

    /// <summary> Updates the component at the given dense index using a custom update function. </summary>
    /// <param name="denseIndex">The dense index of the component.</param>
    /// <param name="updater">The function to update the component.</param>
    public void UpdateByDenseIndex(int denseIndex, Engine.ECS.Components.ComponentUpdater<T> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);

        updater(ref _denseComponents[denseIndex]);
        MarkChanged(denseIndex);
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
        MarkChanged(denseIndex);
    }

    /// <summary> Increments the version of the component at the given dense index. </summary>
    /// <remarks>Also what a raw GetByDenseIndex mutation must call afterward -- it's how ComponentChanged observers see that write.</remarks>
    /// <param name="denseIndex">The dense index of the component.</param>
    public void IncrementVersionByDenseIndex(int denseIndex) => MarkChanged(denseIndex);

    /// <summary>Every write's single exit: bumps the version and tells ComponentChanged observers. Add sets version 1 itself and fires ComponentChanged directly.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MarkChanged(int denseIndex)
    {
        _denseVersions[denseIndex]++;
        ComponentChanged?.Invoke(_denseIndexToEntityIdMap[denseIndex], denseIndex);
    }

    /// <summary>Removes the component for the specified entity if it exists.</summary>
    /// <remarks>Swaps the last dense slot into the freed one to keep dense storage contiguous, then re-patches the moved entity's own index mapping -- see MultiComponentPool.RemoveDenseIndexInternal for the same approach with an added linked-chain relink step.</remarks>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <returns>True if the component was removed, false otherwise.</returns>
    public bool Remove(int entityId)
    {
        var denseIndex = _denseIndexByEntity.Get(entityId);
        if (denseIndex < 0)
        {
            return false;
        }

        ComponentRemoving?.Invoke(entityId, denseIndex);

        var lastDenseIndex = _count - 1;

        if (denseIndex != lastDenseIndex)
        {
            var movedEntityId = _denseIndexToEntityIdMap[lastDenseIndex];

            _denseComponents[denseIndex] = _denseComponents[lastDenseIndex];
            _denseIndexToEntityIdMap[denseIndex] = movedEntityId;
            _denseVersions[denseIndex] = _denseVersions[lastDenseIndex];

            _denseIndexByEntity.GetWritable(movedEntityId) = denseIndex;
        }

        _denseIndexByEntity.GetWritable(entityId) = -1;

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

    /// <inheritdoc/>
    public void ReserveDenseCapacity(int minimumCount)
    {
        if (minimumCount > _denseComponents.Length)
        {
            GrowDenseTo(minimumCount);
        }
    }

    /// <summary> Grows dense storage (see DenseCapacityGrowth) if it's currently full. </summary>
    private void EnsureDenseCapacityForOneMore()
    {
        if (_count < _denseComponents.Length)
        {
            return;
        }

        GrowDenseTo(DenseCapacityGrowth.Next(_denseComponents.Length));
    }

    private void GrowDenseTo(int newSize)
    {
        Array.Resize(ref _denseComponents, newSize);
        Array.Resize(ref _denseIndexToEntityIdMap, newSize);
        Array.Resize(ref _denseVersions, newSize);

        for (var i = _count; i < newSize; i++)
        {
            _denseIndexToEntityIdMap[i] = -1;
        }
    }
}
