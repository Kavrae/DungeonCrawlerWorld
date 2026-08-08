namespace Game.Modules.Core.Components;

/// <summary>
/// What kind(s) of non-Blocking behavior a NonBlockingComponent instance grants, beyond the
/// map-collision exemption itself: Tiny draws the entity in the tile's 3x3 tiny-entity grid,
/// Phasing draws it at 50% alpha. Both may be set (a tiny ghost is both). Flags, not separate
/// bools on the component, so a future kind is one new value here rather than a new field
/// threaded through every constructor call site and every place that combines multiple
/// stacked instances together (see NonBlockingQueries.CombinedKind).
/// </summary>
[Flags]
public enum NonBlockingKind
{
    None = 0,
    Tiny = 1 << 0,
    Phasing = 1 << 1,
}

/// <summary>
/// One instance = one independent source (an ability, a racial trait, ...) currently keeping
/// its entity exempt from map collision. Multi-pooled so overlapping sources are handled by
/// the pool's own per-instance Add/RemoveFirst -- an entity stays exempt as long as at least
/// one instance remains, regardless of how many others expire. See IMapQuery.IsBlocking,
/// which is what actually derives blocking/non-blocking from this and ForceBlockingComponent.
///
/// Kind additionally records how MapWindow should render the entity while exempt -- this used
/// to be a wholly separate OccupancyComponent, which could be (and once was, on Ghost) added
/// without its required NonBlockingComponent counterpart, silently doing nothing. Folding the
/// rendering kind into the exemption grant itself makes that impossible: there is no longer a
/// second thing to remember. A source contributing NonBlockingKind.None grants the collision
/// exemption but no rendering behavior -- callers should not add an instance for that; if a
/// source's Kind would become None, remove its instance entirely rather than leave a hollow
/// one behind. This keeps "does this entity have any NonBlockingComponent" (what IsBlocking
/// actually reads) meaningful, but is a construction-time discipline, not something this type
/// or IsBlocking enforces itself.
/// </summary>
public struct NonBlockingComponent(NonBlockingKind kind = NonBlockingKind.None)
{
    public NonBlockingKind Kind = kind;

    public override readonly string ToString() => $"Kind : {Kind}";
}
