using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier.Components;
using Game.World;

namespace Game.Modules.ProcessingTier.Systems;

/// <summary>
/// Computes each movement-capable entity's own ProcessingTierComponent, and raises
/// ProcessingTierEvents.TierChanged whenever it differs from last time, so any number of hot
/// systems can migrate the entity between their own TieredEntityStripeSet buckets instead of
/// each recomputing distance itself. Adding a 9th or 10th consumer costs nothing here at all --
/// see TODO.md's "Distance-based processing" entry, which this implements.
///
/// Self-tiered: this system's own recompute cadence for an entity is throttled by that same
/// entity's last-known tier, via its own TieredEntityStripeSet wired against its own _tiers
/// pool (the same ProcessingTierWiring.CreateAndWire every other consumer uses, just fed back
/// into itself) -- a Beyond-tier entity gets its own tier rechecked at Beyond's cadence, not at
/// the uniform base cadence every entity used to get regardless of how irrelevant it currently
/// is. This trades a bounded, self-correcting staleness (a promotion can lag up to one full
/// coarse-tier period behind the player closing distance -- e.g. up to StripeCount * 8 frames
/// for a Beyond entity -- before its next check catches it up, after which its own cadence
/// speeds up immediately) for reusing the exact same infrastructure as every other consumer,
/// rather than a bespoke spatial index. A newly-added mover has no ProcessingTierComponent yet,
/// so it fails open to Beyond (TieredEntityStripeSet.OnMemberAdded's lookup delegate, see
/// ProcessingTierWiring's own doc comment) -- the slowest cadence, not the fastest -- until its
/// own first real computation lands. Bulk population creates the overwhelming majority of the
/// game's entities all at once, before this system has run even a single time, so "unknown"
/// needs to default cheap: assuming every one of them might be right next to the player would
/// make the exact same startup cost this system exists to avoid, just deferred by zero frames
/// instead of avoided. A newly-spawned entity that's genuinely close to the player pays the same
/// bounded promotion lag any other entity closing distance already does (see the Local-entry
/// paragraph below) -- self-correcting within one coarse-tier period, not a permanent
/// misclassification.
///
/// Four tiers, same-layer (Vector3Int.Z) entities only get past the first check -- a different
/// MapLayer (Ground/UnderGround/Flying) is never visible to the player regardless of X/Y, so
/// it's always Beyond:
/// - Local: Chebyshev (X/Y) distance from the player &lt;= LocalRadiusTiles to enter; once Local,
///   stays until distance exceeds LocalRadiusTiles + LocalExitBufferTiles to exit. This
///   hysteresis keeps an entity pacing near the boundary alongside the player from migrating
///   TieredEntityStripeSet buckets every recompute -- a tier that flapped every visit would make
///   wander pacing visibly inconsistent right at the edge (and pay the migration cost needlessly).
/// - Neighborhood: not Local, but the same fixed 1000x1000-tile grid cell as the player.
/// - Borough: not Neighborhood, but the same fixed 2000x2000-tile (2x2 neighborhoods) grid cell.
/// - Beyond: everything else.
///
/// Membership (which entities get a tier at all) is driven off MovementComponent for now --
/// MovementSystem is the only consumer so far. Revisit if a future consumer needs to throttle
/// entities that don't wander (e.g. a stationary hazard emitter).
/// </summary>
public sealed class ProcessingTierSystem : ISystem
{
    private const byte StripeCountValue = 15;

    public byte StripeCount => StripeCountValue;

    private const int LocalRadiusTiles = 80;
    private const int LocalExitBufferTiles = 16;
    private const int NeighborhoodSizeTiles = 1000;
    private const int BoroughSizeTiles = 2000;

    private readonly DirectComponentPool<TransformComponent> _transforms;
    private readonly DirectComponentPool<ProcessingTierComponent> _tiers;
    private readonly IPlayerQuery? _playerQuery;
    private readonly ProcessingTierEvents _events;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public ProcessingTierSystem(
        DirectComponentPool<TransformComponent> transforms,
        PackedComponentPool<MovementComponent> movementComponents,
        DirectComponentPool<ProcessingTierComponent> tiers,
        IPlayerQuery? playerQuery,
        ProcessingTierEvents events)
    {
        _transforms = transforms;
        _tiers = tiers;
        _playerQuery = playerQuery;
        _events = events;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, movementComponents, tiers, events);
    }

    /// <summary>Nothing to do before a real player position exists -- consumers treat an absent ProcessingTierComponent as Beyond (see TieredEntityStripeSet.OnMemberAdded / ProcessingTierWiring's own doc comment), so leaving every entity untiered until spawn is the correct default, not a special case.</summary>
    public void Update(EngineTime time, byte stripeIndex)
    {
        if (_playerQuery is null || !_transforms.TryGetReadonly(_playerQuery.PlayerEntityId, out var playerTransform))
        {
            return;
        }

        foreach (var entityId in _tieredStripeSet.GetDueEntities(time.FrameCount))
        {
            if (!_transforms.TryGetReadonly(entityId, out var transform))
            {
                continue;
            }

            var hasExisting = _tiers.TryGetReadonly(entityId, out var existing);
            var previousTier = hasExisting ? existing.Tier : (ProcessingTierLevel?)null;
            var tier = ComputeTier(transform.Position, playerTransform.Position, previousTier);

            if (hasExisting)
            {
                _tiers.TryUpdate(entityId, tier, static (ref ProcessingTierComponent component, ProcessingTierLevel newTier) => component = new ProcessingTierComponent(newTier));
            }
            else
            {
                _tiers.Add(entityId, new ProcessingTierComponent(tier));
            }

            if (!hasExisting || existing.Tier != tier)
            {
                _events.RaiseTierChanged(entityId, tier);
            }
        }
    }

    private static ProcessingTierLevel ComputeTier(Vector3Int position, Vector3Int playerPosition, ProcessingTierLevel? previousTier)
    {
        if (position.Z != playerPosition.Z)
        {
            return ProcessingTierLevel.Beyond;
        }

        var distance = Math.Max(Math.Abs(position.X - playerPosition.X), Math.Abs(position.Y - playerPosition.Y));
        var localRadius = previousTier == ProcessingTierLevel.Local ? LocalRadiusTiles + LocalExitBufferTiles : LocalRadiusTiles;

        if (distance <= localRadius)
        {
            return ProcessingTierLevel.Local;
        }

        if (SameCell(position, playerPosition, NeighborhoodSizeTiles))
        {
            return ProcessingTierLevel.Neighborhood;
        }

        if (SameCell(position, playerPosition, BoroughSizeTiles))
        {
            return ProcessingTierLevel.Borough;
        }

        return ProcessingTierLevel.Beyond;
    }

    /// <summary>Map positions are always non-negative (grid coordinates within [0, MapSize)), so plain integer division already floors correctly -- no sign handling needed to find which fixed cellSize-tile grid cell a position falls into.</summary>
    private static bool SameCell(Vector3Int position, Vector3Int playerPosition, int cellSize) =>
        position.X / cellSize == playerPosition.X / cellSize && position.Y / cellSize == playerPosition.Y / cellSize;
}
