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
/// Maintained incrementally from three events, never by scanning: the driving pool's EntityAdded
/// (entities are born already tiered -- ProcessingTierResolver.CreateEntityAt writes the tier as
/// their first component -- so a mover born Local is admitted here, when it gains
/// MovementComponent, without any TierChanged ever firing for it), ProcessingTierEvents.TierChanged
/// for every later change, and the driving pool's EntityRemoved so a destroyed entity -- or one
/// that loses the driving component -- leaves immediately rather than lingering as a stale id.
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
/// Membership is movers only, by choice. Every positioned entity is now tiered (see
/// ProcessingTierSystem), so a stationary entity -- a shop, a container, lava -- does have a correct
/// tier, but it is deliberately kept out of this roster: the roster's whole value is being the small
/// side, and the terrain alone within the Local radius would make it roughly fifty times larger.
/// Fine for every consumer so far (only movers queue windups). A consumer that needs stationary
/// Local entities should read their ProcessingTierComponent directly rather than widen this.
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

    private IEntityMembershipPool? _drivingPool;
    private IReadOnlyComponentPool<ProcessingTierComponent>? _tiers;

    /// <summary>Subscribes this roster to a driving pool's membership and to tier changes -- mirrors ProcessingTierWiring.CreateAndWire's role for TieredEntityStripeSet, kept as a method on the roster itself since (unlike a stripe set) there is exactly one of these per game rather than one per consuming system.</summary>
    /// <param name="drivingPool">The pool whose members this roster tracks -- MovementComponent. Tiering itself now covers every positioned entity (see ProcessingTierSystem), but this roster deliberately stays scoped to movers: it exists to be the small side of "Local AND pending something", and admitting the terrain within the Local radius would make it roughly fifty times larger and defeat that.</param>
    /// <param name="tiers">Read when an entity joins the driving pool -- see OnEntityAdded.</param>
    /// <param name="processingTierEvents">The shared tier-change event source.</param>
    public void Wire(IEntityMembershipPool drivingPool, IReadOnlyComponentPool<ProcessingTierComponent> tiers, ProcessingTierEvents processingTierEvents)
    {
        ArgumentNullException.ThrowIfNull(drivingPool);
        ArgumentNullException.ThrowIfNull(tiers);
        ArgumentNullException.ThrowIfNull(processingTierEvents);

        _drivingPool = drivingPool;
        _tiers = tiers;

        drivingPool.EntityAdded += OnEntityAdded;
        drivingPool.EntityRemoved += OnEntityRemoved;
        processingTierEvents.TierChanged += OnTierChanged;
    }

    /// <summary>
    /// Entities now arrive already tiered: ProcessingTierResolver.CreateEntityAt writes the tier as
    /// an entity's first component, silently, before its blueprint adds MovementComponent. So a new
    /// mover that is born Local never raises TierChanged, and the only moment this roster can learn
    /// about it is here, when it joins the driving pool. (This used to have no EntityAdded handler at
    /// all, on the grounds that a newly-added entity had no tier yet -- true of the old periodic
    /// scan, false under tier-first.) An entity that genuinely has no tier yet still stays out,
    /// matching IsLocal's "unknown is not Local".
    /// </summary>
    private void OnEntityAdded(int entityId)
    {
        if (_tiers!.TryGetReadonly(entityId, out var tier) && tier.Tier == ProcessingTierLevel.Local && _localEntityIds.Add(entityId))
        {
            _snapshotStale = true;
        }
    }

    private void OnEntityRemoved(int entityId)
    {
        if (_localEntityIds.Remove(entityId))
        {
            _snapshotStale = true;
        }
    }

    /// <summary>TierChanged now fires for every positioned entity, terrain included, so this filters to driving-pool members -- see Wire's own note on why the roster stays movers-only.</summary>
    private void OnTierChanged(int entityId, ProcessingTierLevel tier)
    {
        bool changed;
        if (tier == ProcessingTierLevel.Local)
        {
            changed = _drivingPool!.Has(entityId) && _localEntityIds.Add(entityId);
        }
        else
        {
            changed = _localEntityIds.Remove(entityId);
        }

        if (changed)
        {
            _snapshotStale = true;
        }
    }
}
