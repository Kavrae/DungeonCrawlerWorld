namespace Game.Modules.Core.Components;

/// <summary>
/// One instance = one independent source (an ability, a racial trait, ...) currently keeping
/// its entity exempt from map collision. Multi-pooled so overlapping sources are handled by
/// the pool's own per-instance Add/RemoveFirst -- an entity stays exempt as long as at least
/// one instance remains, regardless of how many others expire. See IMapQuery.IsBlocking,
/// which is what actually derives blocking/non-blocking from this and ForceBlockingComponent.
/// </summary>
public struct NonBlockingComponent;
