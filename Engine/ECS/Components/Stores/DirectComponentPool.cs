using System.Runtime.CompilerServices;

namespace Engine.ECS.Components.Stores;

/// <summary> Entity-indexed component storage for near-universal components. </summary>
/// <remarks> Storage index == entityId. Best for components present on most entities, where direct lookup beats sparse-set indirection. </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class DirectComponentPool<T> : IReadOnlyComponentPool<T>, IInspectableComponentPool where T : struct
{
    private T[] _components;
    private byte[] _present;
    private uint[] _versions;
    private readonly MergeAction<T> _mergeImplementation;
    private int _count;

    /// <summary> The type of component stored in this pool. </summary>
    public Type ComponentType => typeof(T);

    /// <summary> The number of components in the pool. </summary>
    public int Count => _count;

    /// <summary> The number of components the pool can hold before resizing </summary>
    public int Capacity => _components.Length;

    /// <summary> A read-only span of the components in the pool indexed by entityId. </summary>
    public ReadOnlySpan<T> Components => _components;

    /// <summary> A read-only span indicating which entities have components indexed by entityId. </summary>
    public ReadOnlySpan<byte> Present => _present;

    /// <summary> A read-only span of the versions for the components in the pool indexed by entityId. </summary>
    public ReadOnlySpan<uint> Versions => _versions;

    /// <summary> A delegate that defines a method for updating a component to a given state. </summary>
    public delegate void ComponentUpdater<TState>(ref T component, TState state);

    /// <summary> Initializes a new instance of the <see cref="DirectComponentPool{T}"/> class with the specified initial capacity and merge implementation. </summary>
    /// <param name="initialCapacity">The initial pool size based on a maximum EntityId</param>
    /// <param name="mergeImplementation">Determines how two instances of a component should be merged together.</param>
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

    /// <summary> Resizes the pool to accommodate the new maximum entity count. </summary>
    /// <param name="newMaximumEntityCount">The new maximum entity count.</param>
    public void Resize(int newMaximumEntityCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(newMaximumEntityCount, _components.Length);

        Array.Resize(ref _components, newMaximumEntityCount);
        Array.Resize(ref _present, newMaximumEntityCount);
        Array.Resize(ref _versions, newMaximumEntityCount);
    }

    /// <summary> Adds a component to the pool for the specified entity. </summary>
    /// <param name="entityId">The ID of the entity to add the component to.</param>
    /// <param name="newComponent">The component to add.</param>
    public void Add(int entityId, T newComponent)
    {
        if (_present[entityId] != 0)
        {
            throw new InvalidOperationException($"Entity {entityId} already has a component of type {typeof(T).Name}.");
        }

        _components[entityId] = newComponent;
        _present[entityId] = 1;
        _versions[entityId] = 1;
        _count++;
    }

    /// <summary> Merges a component with the existing component for the specified entity. </summary>
    /// <remarks>If the entity does not have a component of this type, it will be added instead.
    /// The merge implementation defines how this component should handle each property.</remarks>
    /// <param name="entityId">The ID of the entity to merge the component with.</param>
    /// <param name="newComponent">The component to merge.</param>
    public void Merge(int entityId, T newComponent)
    {
        if (_present[entityId] != 0)
        {
            _mergeImplementation(ref _components[entityId], newComponent);
            _versions[entityId]++;
            return;
        }

        Add(entityId, newComponent);
    }

    /// <summary>True if the specified entity has a component of this type</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    private bool IsInBounds(int entityId) => (uint)entityId < (uint)_present.Length;

    /// <summary>True if the specified entity has a component of this type</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    public bool Has(int entityId) => IsInBounds(entityId) && _present[entityId] != 0;

    /// <summary>Attempts to get a readonly reference to the component for the specified entity.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="component">Stores a readonly reference to the component if the entity has one, or the default value if not.</param>
    public bool TryGetReadonly(int entityId, out T component)
    {
        if (!IsInBounds(entityId) || _present[entityId] == 0)
        {
            component = default;
            return false;
        }

        component = _components[entityId];
        return true;
    }

    /// <summary>Gets a readonly reference to the component for the specified entity.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    public ref readonly T GetReadonly(int entityId)
    {
        if (_present[entityId] == 0)
        {
            throw new InvalidOperationException($"Entity {entityId} does not have component {typeof(T).Name}.");
        }

        return ref _components[entityId];
    }

    /// <summary> Hot-path mutable access to the component.</summary>
    /// <remarks> WARNING : Caller must manually increment version after mutation. Prefer TryUpdate/TrySet unless you are in a tight loop. </remarks>
    public ref T Get(int entityId)
    {
        if (_present[entityId] == 0)
        {
            throw new InvalidOperationException($"Entity {entityId} does not have component {typeof(T).Name}.");
        }

        return ref _components[entityId];
    }

    /// <summary>Returns the string representation of the component for the specified entity.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="destination">The list to add the inspection data to.</param>
    /// <returns>The number of inspection entries added.</returns>
    public int CopyInspectionDataForEntity(int entityId, List<InspectedComponentEntry> destination)
    {
        if (_present[entityId] == 0)
        {
            return 0;
        }

        destination.Add(new InspectedComponentEntry(
            ComponentType,
            _components[entityId].ToString() ?? string.Empty,
            _versions[entityId]));

        return 1;
    }

    /// <summary>Gets the version of the component for the specified entity.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <returns>The version of the component.</returns>
    public uint GetVersion(int entityId)
    {
        if (_present[entityId] == 0)
        {
            throw new InvalidOperationException($"Entity {entityId} does not have component {typeof(T).Name}.");
        }

        return _versions[entityId];
    }

    /// <summary>Attempts to set the component for the specified entity if the entityId is in bounds and already contains the component.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>True if the component was set, false otherwise.</returns>
    public bool TrySet(int entityId, T value)
    {
        if (!IsInBounds(entityId) || _present[entityId] == 0)
        {
            return false;
        }

        _components[entityId] = value;
        _versions[entityId]++;
        return true;
    }

    /// <summary>Attempts to update the component for the specified entity using a custom update function if the entityId is in bounds and already contains the component.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="updater">The function to update the component.</param>
    /// <returns>True if the component was updated, false otherwise.</returns>
    public bool TryUpdate(int entityId, Engine.ECS.Components.ComponentUpdater<T> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);

        if (!IsInBounds(entityId) || _present[entityId] == 0)
        {
            return false;
        }

        updater(ref _components[entityId]);
        _versions[entityId]++;
        return true;
    }

    /// <summary>Attempts to update the component for the specified entity using a custom update function and state if the entityId is in bounds and already contains the component.</summary>
    /// <typeparam name="TState">The type of the state parameter.</typeparam>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="state">The state to pass to the update function.</param>
    /// <param name="updater">The function to update the component.</param>
    /// <returns>True if the component was updated, false otherwise.</returns>
    public bool TryUpdate<TState>(int entityId, TState state, ComponentUpdater<TState> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);

        if (!IsInBounds(entityId) || _present[entityId] == 0)
        {
            return false;
        }

        updater(ref _components[entityId], state);
        _versions[entityId]++;
        return true;
    }

    /// <summary>Increments the version of the component for the specified entity.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    public void IncrementVersion(int entityId)
    {
        if (_present[entityId] == 0)
        {
            throw new InvalidOperationException($"Entity {entityId} does not have component {typeof(T).Name}.");
        }

        _versions[entityId]++;
    }

    /// <summary>Removes the component for the specified entity if it exists.</summary>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <returns>True if the component was removed, false otherwise.</returns>
    public bool Remove(int entityId)
    {
        if (!IsInBounds(entityId) || _present[entityId] == 0)
        {
            return false;
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _components[entityId] = default;
        }

        _present[entityId] = 0;
        _versions[entityId] = 0;
        _count--;
        return true;
    }

    /// <summary>Clears all components from the pool.</summary>
    public void Clear()
    {
        Array.Clear(_components);
        Array.Clear(_present);
        Array.Clear(_versions);
        _count = 0;
    }
}
