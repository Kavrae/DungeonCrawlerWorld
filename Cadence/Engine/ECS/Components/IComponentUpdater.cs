namespace Engine.ECS.Components;

/// <summary>Changes a component in place: a plain value carrying its own inputs, applied by a pool's TryUpdate, UpdateByDenseIndex or TryUpdateFirst.</summary>
/// <remarks>
/// Pools take it as an unmanaged type argument, so the JIT specialises and inlines Update at every call, and an updater can hold
/// no pool, bus or other reference: its result depends only on the component and its own fields. Read anything else before
/// building the updater.
///
/// Because it is unmanaged, an updater can't carry a new value for a reference-typed field (an override definition, a string).
/// Write such a field as the whole component instead: TrySet or SetByDenseIndex on a Direct or Packed pool, or, in a Multi pool,
/// assign through GetByDenseIndex and then call IncrementVersionByDenseIndex so the write is versioned and observed like any other.
/// </remarks>
public interface IComponentUpdater<T> where T : struct
{
    void Update(ref T component);
}

/// <summary>Picks out one of an entity's components in a Multi pool: TryGetFirst, RemoveFirst, CountMatching and TryUpdateFirst test each of the entity's components with Matches.</summary>
/// <remarks>Taken as an unmanaged type argument for the same reasons as IComponentUpdater: Matches is inlined at every call and depends only on the component and the matcher's own fields.</remarks>
public interface IComponentMatcher<T> where T : struct
{
    bool Matches(in T component);
}
