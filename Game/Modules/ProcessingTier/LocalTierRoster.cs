using Engine.ECS.Components;
using Game.Modules.ProcessingTier.Components;

namespace Game.Modules.ProcessingTier;

/// <summary>
/// The live set of entities currently at ProcessingTierLevel.Local -- an O(1) "is this entity
/// Local?" answer and a small, directly-iterable membership list, for consumers that need to
/// act on only the Local population rather than to visit every entity at a tier-proportional
/// cadence.
/// </summary>
/// <remarks>
/// Deliberately a different abstraction from TieredEntityStripeSet/ProcessingTierWiring, not a
/// replacement for them. Those solve *scheduling* ("visit this entity every N frames, N chosen
/// by its tier") and their per-tier buckets are both per-consumer (each wired to its own driving
/// pool) and striped, so GetTierBucket answers "this frame's slice of that tier," never "that
/// tier's whole membership." This solves *filtering* ("act on the Local entities, ignore the
/// rest"), which that shape can't express. Both are driven off the exact same signals, so they
/// can't disagree about who is Local.
///
/// Maintained incrementally from three events, never by scanning: ProcessingTierEvents.
/// TierChanged (raised by ProcessingTierSystem on an entity's first computation as well as on
/// every later change, so a newly-tiered entity lands here without any bootstrap pass), plus the
/// driving pool's own EntityAdded/EntityRemoved so a destroyed entity -- or one that loses the
/// driving component -- leaves immediately rather than lingering as a stale id.
///
/// Sizing is what makes materializing this worthwhile: Local is a Chebyshev radius of 80 on the
/// player's own Z only (see ProcessingTierSystem), so at this game's real population density it
/// holds on the order of a thousand entities, against pools that hold tens of thousands. A
/// consumer wanting "the pending entities that are Local" is far better off walking this and
/// probing its own pool than walking its pool and probing tiers -- see
/// ActionTargetingController.AllPendingDelayedActionTargets, the first such consumer.
///
/// Two limits worth knowing before adding a consumer:
///
/// Membership is whatever ProcessingTierSystem itself tiers, which today is MovementComponent
/// (see that system's own closing note). A stationary entity -- a shop, a container, a future
/// hazard emitter -- never gains a ProcessingTierComponent at all and therefore never appears
/// here, no matter how close the player stands to it. Fine for every consumer so far (only
/// movers queue windups), but this is a ceiling on the roster, not a bug in it: widening it
/// means widening ProcessingTierSystem's membership, and this class follows automatically.
///
/// Local is not "on screen." Local's radius is 80 tiles; the Team-zoom viewport is roughly 27
/// tiles from centre, so this set covers several times the area the player can actually see.
/// That is correct for gameplay decisions (a deliberately stable boundary, with hysteresis, so
/// an entity walking on-screen isn't mid-transition), but a purely visual consumer should use
/// this only to shrink its candidate set and then reject on the camera's own bounds.
/// </remarks>
public sealed class LocalTierRoster
{
    private readonly HashSet<int> _localEntityIds = [];

    /// <summary>Rebuilt from _localEntityIds only when it has actually changed since the last read -- callers iterate this every frame, and copying an unchanged set each time would give back most of what walking the small side was meant to save.</summary>
    private int[] _snapshot = [];
    private int _snapshotCount;
    private bool _snapshotStale = true;

    /// <summary>Whether entityId is currently Local. An entity with no ProcessingTierComponent yet is not Local -- matching ProcessingTierWiring's own fail-open-to-Beyond default, for the same reason (bulk population creates thousands of untiered entities at once, and treating "unknown" as "right next to the player" is the exact cost tiering exists to avoid).</summary>
    public bool IsLocal(int entityId) => _localEntityIds.Contains(entityId);

    /// <summary>How many entities are currently Local.</summary>
    public int Count => _localEntityIds.Count;

    /// <summary>
    /// The current Local membership, for direct iteration. A snapshot rather than the live set
    /// so a consumer can act on an entity mid-iteration (including anything that retiers or
    /// destroys it) without invalidating its own enumerator -- the same reason
    /// TieredEntityStripeSet hands out spans of its buckets rather than the buckets themselves.
    /// </summary>
    public ReadOnlySpan<int> LocalEntityIds
    {
        get
        {
            if (_snapshotStale)
            {
                if (_snapshot.Length < _localEntityIds.Count)
                {
                    _snapshot = new int[System.Math.Max(_localEntityIds.Count, _snapshot.Length * 2)];
                }

                _localEntityIds.CopyTo(_snapshot);
                _snapshotCount = _localEntityIds.Count;
                _snapshotStale = false;
            }

            return _snapshot.AsSpan(0, _snapshotCount);
        }
    }

    /// <summary>Subscribes this roster to a driving pool's membership and to tier changes -- mirrors ProcessingTierWiring.CreateAndWire's role for TieredEntityStripeSet, kept as a method on the roster itself since (unlike a stripe set) there is exactly one of these per game rather than one per consuming system.</summary>
    /// <param name="drivingPool">The pool whose membership defines which entities can ever be tiered -- must be the same pool ProcessingTierSystem tiers, or this roster will retain ids that system never updates.</param>
    /// <param name="processingTierEvents">The shared tier-change event source.</param>
    public void Wire(IEntityMembershipPool drivingPool, ProcessingTierEvents processingTierEvents)
    {
        ArgumentNullException.ThrowIfNull(drivingPool);
        ArgumentNullException.ThrowIfNull(processingTierEvents);

        drivingPool.EntityRemoved += OnEntityRemoved;
        processingTierEvents.TierChanged += OnTierChanged;
    }

    /// <summary>No EntityAdded counterpart: a newly-added entity has no tier yet, and adding it here would assert the very "unknown means Local" default IsLocal's own doc comment rejects. It enters this set (if it belongs) the moment ProcessingTierSystem's first computation raises TierChanged for it.</summary>
    private void OnEntityRemoved(int entityId)
    {
        if (_localEntityIds.Remove(entityId))
        {
            _snapshotStale = true;
        }
    }

    private void OnTierChanged(int entityId, ProcessingTierLevel tier)
    {
        var changed = tier == ProcessingTierLevel.Local
            ? _localEntityIds.Add(entityId)
            : _localEntityIds.Remove(entityId);

        if (changed)
        {
            _snapshotStale = true;
        }
    }
}
