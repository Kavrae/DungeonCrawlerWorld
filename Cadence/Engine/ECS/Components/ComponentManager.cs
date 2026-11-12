using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
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
    private readonly List<IComponentPool> _poolsInRegistrationOrder = [];
    private readonly Dictionary<Type, object> _relationshipsByLinkType = [];
    private readonly HashSet<Type> _pendingObservationRequests = [];
    private bool _observationRequestsClosed;

    /// <summary>Each registered type's pool by its ComponentTypeSlot, so a typed lookup is an array read rather than a hash of its Type.</summary>
    private SlotEntry[] _slotEntries = [];

    private static int _nextComponentTypeSlot;

    /// <summary>A process-wide index per component type, the same in every manager; a manager's table covers only the slots it registered.</summary>
    private static class ComponentTypeSlot<T> where T : struct
    {
        public static readonly int Index = Interlocked.Increment(ref _nextComponentTypeSlot) - 1;
    }

    /// <summary>A registered type's pool, and whether it is a relationship's target side, which only the relationship writes.</summary>
    private readonly record struct SlotEntry(IComponentPool? Pool, bool IsRelatedSource);

    /// <summary>Initializes a new instance of the <see cref="ComponentManager"/> class.</summary>
    /// <param name="initialEntityCapacity">The initial capacity for indexing component pools based on the estimated number of entities with the component.</param>
    /// <param name="initialComponentCapacity">The dense storage a Packed or Multi pool starts with when its registration gives none; it grows geometrically from there.</param>
    public ComponentManager(int initialEntityCapacity, int initialComponentCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialEntityCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialComponentCapacity);

        _initialEntityCapacity = initialEntityCapacity;
        _initialComponentCapacity = initialComponentCapacity;
        _membershipMasks = new ComponentMembershipMasks(initialEntityCapacity);
    }

    /// <summary>Which pools hold each entity, each pool's bit its index in registration order: what RemoveAllComponents visits.</summary>
    private readonly ComponentMembershipMasks _membershipMasks;

    /// <summary>Every entity's stable key -- see EntityKeys. EntityManager issues and releases them; relationships resolve links through it.</summary>
    public EntityKeys Keys { get; } = new();

    /// <summary>Every relationship type registered here -- see RegisterRelationship.</summary>
    public RelationshipRegistry Relationships { get; } = new();

    /// <summary>The entities EntityManager.DestroyEntity is running for. EntityManager keeps it; relationships refuse them as targets and only detach them as sources.</summary>
    internal HashSet<int> DestroyingEntityIds { get; } = [];

    /// <summary>Returns true if the component type is registered to a pool.</summary>
    public bool IsRegistered<T>() where T : struct => EntryOf<T>().Pool is not null;

    /// <summary>Registers a direct component pool for the specified component type.</summary>
    /// <remarks>Direct component pools are suitable for components that are expected to be present on most entities.</remarks>
    public void RegisterDirectPool<T>(MergeAction<T> mergeAction) where T : struct
    {
        ThrowIfAlreadyRegistered(typeof(T));
        AddPool<T>(new DirectComponentPool<T>(_initialEntityCapacity, mergeAction));
    }

    /// <summary>Registers a packed component pool for the specified component type.</summary>
    /// <remarks>
    /// Packed component pools are suitable for components that are expected to be present on a subset of entities.
    /// Its entity index is paged, so it costs only the id ranges its holders fall in. initialCapacity is the dense
    /// storage it starts with (the manager's default when null); it grows geometrically from there, so a component
    /// known to be common can start larger to skip the early growth steps. observation says whether anything may
    /// subscribe to the pool's changes (see ComponentChangeObservation); an IScheduledTimer component's pool is
    /// always observed.
    /// </remarks>
    public void RegisterPackedPool<T>(MergeAction<T> mergeAction, int? initialCapacity = null, ComponentChangeObservation observation = ComponentChangeObservation.Unobserved) where T : struct
    {
        ThrowIfAlreadyRegistered(typeof(T));
        AddPool<T>(new PackedComponentPool<T>(_initialEntityCapacity, initialCapacity ?? _initialComponentCapacity, mergeAction, observation));
    }

    /// <summary>Registers a multi component pool for the specified component type.</summary>
    /// <remarks>
    /// Multi component pools are suitable for components that can be added multiple times to the same entity.
    /// See RegisterPackedPool's own remarks for initialCapacity and observation.
    /// </remarks>
    public void RegisterMultiPool<T>(int? initialCapacity = null, ComponentChangeObservation observation = ComponentChangeObservation.Unobserved) where T : struct
    {
        ThrowIfAlreadyRegistered(typeof(T));
        AddPool<T>(new MultiComponentPool<T>(_initialEntityCapacity, initialCapacity ?? _initialComponentCapacity, observation));
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

        var links = new PackedComponentPool<TLink>(_initialEntityCapacity, initialCapacity ?? _initialComponentCapacity, static (ref existing, incoming) => existing = incoming, ComponentChangeObservation.Observed);
        var relatedSources = new MultiComponentPool<RelatedSourceComponent<TLink>>(_initialEntityCapacity, initialCapacity ?? _initialComponentCapacity);
        AddPool<TLink>(links);
        AddPool<RelatedSourceComponent<TLink>>(relatedSources, isRelatedSource: true);

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

    /// <summary>Makes T's Packed or Multi pool Observed, whichever module registers it, so the caller can subscribe to its ComponentChanged and ComponentRemoving.</summary>
    /// <remarks>
    /// For a module that observes a pool another module owns: call it in RegisterComponents, before or after the owner registers the
    /// pool. The build closes requests once every module has registered its components; a request for a type no module registered
    /// then fails the build.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Component registration has closed, or T is registered as a Direct pool, which raises no change events.</exception>
    public void ObserveChanges<T>() where T : struct
    {
        if (_observationRequestsClosed)
        {
            throw new InvalidOperationException($"Observation of {typeof(T).Name} was requested after component registration closed; request it in RegisterComponents.");
        }

        if (_componentPools.TryGetValue(typeof(T), out var pool))
        {
            MarkObserved(typeof(T), pool);
            return;
        }

        _pendingObservationRequests.Add(typeof(T));
    }

    /// <summary>Ends ObserveChanges requests: every module has registered its components.</summary>
    /// <exception cref="InvalidOperationException">A requested type was never registered.</exception>
    internal void CloseObservationRequests()
    {
        _observationRequestsClosed = true;
        if (_pendingObservationRequests.Count > 0)
        {
            throw new InvalidOperationException($"Observation was requested for component types no module registered: {string.Join(", ", _pendingObservationRequests.Select(type => type.Name))}.");
        }
    }

    private void AddPool<T>(IComponentPool pool, bool isRelatedSource = false) where T : struct
    {
        var componentType = typeof(T);
        _componentPools.Add(componentType, pool);
        var membershipBit = _poolsInRegistrationOrder.Count;
        _poolsInRegistrationOrder.Add(pool);
        _membershipMasks.EnsureBitCount(_poolsInRegistrationOrder.Count);
        ((IMembershipMaskedComponentPool)pool).AttachMembershipMasks(_membershipMasks, membershipBit);

        var slot = ComponentTypeSlot<T>.Index;
        if (slot >= _slotEntries.Length)
        {
            Array.Resize(ref _slotEntries, System.Math.Max(slot + 1, System.Math.Max(16, _slotEntries.Length * 2)));
        }

        _slotEntries[slot] = new SlotEntry(pool, isRelatedSource);

        if (_pendingObservationRequests.Remove(componentType))
        {
            MarkObserved(componentType, pool);
        }
    }

    /// <summary>T's registration here, or an empty entry when T has none: an array read by T's process-wide slot.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SlotEntry EntryOf<T>() where T : struct
    {
        var slot = ComponentTypeSlot<T>.Index;
        var entries = _slotEntries;
        return (uint)slot < (uint)entries.Length ? entries[slot] : default;
    }

    private static void MarkObserved(Type componentType, IComponentPool pool)
    {
        switch (pool)
        {
            case IObservableComponentPool observable:
                observable.MarkObserved();
                break;
            default:
                throw new InvalidOperationException($"{componentType.Name} is registered as a {pool.GetType().Name[..pool.GetType().Name.IndexOf('`')]}, which raises no change events; it can't be observed.");
        }
    }

    private void ThrowIfAlreadyRegistered(Type componentType)
    {
        if (_componentPools.ContainsKey(componentType))
        {
            throw new InvalidOperationException($"Component type {componentType.Name} is already registered.");
        }
    }

    [DoesNotReturn]
    private static void ThrowNotRegistered<T>() => throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered.");

    [DoesNotReturn]
    private static void ThrowRegisteredAsOther<T>(string poolKind) => throw new InvalidOperationException($"Component type {typeof(T).Name} is not registered as a {poolKind} component pool.");

    [DoesNotReturn]
    private static void ThrowRelatedSourceComponentType<T>() => throw new InvalidOperationException($"{typeof(T).Name} is kept by its relationship and can't be written directly; write the link, and read a target's sources through GetRelationship.");

    /// <summary>T's pool, throwing when T is unregistered or is a relationship's target side, which only its relationship writes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IComponentPool WritablePoolOf<T>() where T : struct
    {
        var entry = EntryOf<T>();
        if (entry.IsRelatedSource)
        {
            ThrowRelatedSourceComponentType<T>();
        }

        if (entry.Pool is null)
        {
            ThrowNotRegistered<T>();
        }

        return entry.Pool;
    }

    /// <summary>Retrieves the direct component pool for the specified component type.</summary>
    /// <remarks>Used when the component is known to be registered to a direct pool.</remarks>
    public DirectComponentPool<T> GetDirectPool<T>() where T : struct
    {
        var pool = EntryOf<T>().Pool;
        if (pool is null)
        {
            ThrowNotRegistered<T>();
        }

        if (pool is not DirectComponentPool<T> typedStore)
        {
            ThrowRegisteredAsOther<T>("direct");
            return null;
        }

        return typedStore;
    }

    /// <summary>Retrieves the packed component pool for the specified component type.</summary>
    /// <remarks>Used when the component is known to be registered to a packed pool.</remarks>
    public PackedComponentPool<T> GetPackedPool<T>() where T : struct
    {
        var pool = EntryOf<T>().Pool;
        if (pool is null)
        {
            ThrowNotRegistered<T>();
        }

        if (pool is not PackedComponentPool<T> typedStore)
        {
            ThrowRegisteredAsOther<T>("packed");
            return null;
        }

        return typedStore;
    }

    /// <summary>Retrieves the multi component pool for the specified component type.</summary>
    /// <remarks>Used when the component is known to be registered to a multi pool.</remarks>
    public MultiComponentPool<T> GetMultiPool<T>() where T : struct
    {
        if (WritablePoolOf<T>() is not MultiComponentPool<T> typedStore)
        {
            ThrowRegisteredAsOther<T>("multi");
            return null;
        }

        return typedStore;
    }

    /// <summary>Retrieves the read-only component pool for the specified component type regardless of its registration type.</summary>
    /// <exception cref="InvalidOperationException">T is not registered.</exception>
    public IReadOnlyComponentPool<T> GetReadOnlyPool<T>() where T : struct
    {
        var pool = EntryOf<T>().Pool;
        if (pool is null)
        {
            ThrowNotRegistered<T>();
        }

        return (IReadOnlyComponentPool<T>)pool;
    }

    /// <summary> Adds or merges a component without the caller needing to know which pool type T was registered as</summary>
    /// <remarks>Direct and Packed pools merge with any existing component; Multi pools have no single existing value to merge into, so every call is an Add.</remarks>
    public void Merge<T>(int entityId, T component) where T : struct
    {
        switch (WritablePoolOf<T>())
        {
            case PackedComponentPool<T> packed:
                packed.Merge(entityId, component);
                break;
            case MultiComponentPool<T> multi:
                multi.Add(entityId, component);
                break;
            case DirectComponentPool<T> direct:
                direct.Merge(entityId, component);
                break;
            default:
                throw new InvalidOperationException($"Component type {typeof(T).Name} is registered as an unsupported pool type for Merge.");
        }
    }

    /// <summary>Every registered pool, in registration order: the order RemoveAllComponents removes in.</summary>
    public IReadOnlyList<IComponentPool> PoolsInRegistrationOrder => _poolsInRegistrationOrder;

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

        _membershipMasks.EnsureEntityCapacity(newMaximumEntityCount);
    }

    /// <summary>Removes a component of the specified type from the entity</summary>
    public bool RemoveComponent<T>(int entityId) where T : struct
    {
        return WritablePoolOf<T>().Remove(entityId);
    }

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

    /// <summary>Removes every component the entity holds, in every pool, in registration order.</summary>
    /// <remarks>
    /// Visits only the pools the entity's membership mask names. The mask is read again after each removal, from the
    /// next pool on, so a removal handler that adds the entity to a later pool is still followed -- what visiting every
    /// pool in order would have done.
    /// </remarks>
    public void RemoveAllComponents(int entityId)
    {
        var pools = _poolsInRegistrationOrder;
        var membershipMasks = _membershipMasks;
        for (var wordIndex = 0; wordIndex < membershipMasks.WordsPerEntity; wordIndex++)
        {
            var pending = membershipMasks.WordAt(entityId, wordIndex);
            while (pending != 0)
            {
                var bitInWord = BitOperations.TrailingZeroCount(pending);
                pools[(wordIndex << 6) + bitInWord].Remove(entityId);
                pending = bitInWord == 63 ? 0 : membershipMasks.WordAt(entityId, wordIndex) & (ulong.MaxValue << (bitInWord + 1));
            }
        }
    }
}