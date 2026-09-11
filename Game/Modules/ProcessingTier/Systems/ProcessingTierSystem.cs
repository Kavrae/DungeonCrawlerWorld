using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.ProcessingTier.Components;
using Game.World;

namespace Game.Modules.ProcessingTier.Systems;

/// <summary>
/// Keeps every positioned entity's ProcessingTierComponent correct as things move, and raises
/// ProcessingTierEvents.TierChanged whenever one changes, so every TieredEntityStripeSet migrates
/// the entity between its own buckets without recomputing distance itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>Event-driven, not a scan.</b> This used to walk its own tiered stripe set every frame,
/// recomputing distance for each due entity -- and its membership was MovementComponent only, so
/// any stationary entity in a tiered consumer's pool (lava, shops, treasure chests) was never tiered
/// at all and sat at the fail-open Beyond default permanently. See PLAN-processing-tier-rework.md.
/// Membership is now every entity with a TransformComponent, which is only affordable because the
/// scan is gone: at ~2.08M positioned entities, a periodic scan would cost more than the entire rest
/// of the simulation.
/// </para>
/// <para>
/// Tiers are assigned at creation (ProcessingTierResolver.CreateEntityAt, or EnsureTiered via
/// World.EntityPlaced), so this system only handles <i>change</i>, from exactly two sources:
/// <list type="bullet">
/// <item><b>An entity moved.</b> Drained from the shared FrameEventBuffer&lt;EntityMovedEvent&gt;
/// MovementSystem records every move into -- O(1) per mover. Deliberately buffered rather than
/// retiered synchronously at the move: MovementSystem is iterating one of its own tier buckets as a
/// span when it moves an entity, and a synchronous TierChanged would swap-remove from that very list
/// mid-iteration. This is why ProcessingTierModule declares a dependency on MovementModule: this
/// system has to run after MovementSystem each frame, or it drains a buffer that has already been
/// cleared.</item>
/// <item><b>The player moved.</b> Neighborhood, Borough and Beyond are fixed absolute grid cells,
/// so they only change when the player crosses a cell boundary or changes MapLayer -- then this does
/// a full rebuild, which on the current 1000x1000 map never happens. Local is the only
/// player-relative tier, and a player step changes Local membership only along the edges. Those
/// edges are walked through the map's own position index (blocking occupant, non-blocking
/// occupants, terrain) rather than by visiting every entity.</item>
/// </list>
/// </para>
/// <para>
/// <b>Hysteresis means two edges, not one.</b> Promotion crosses LocalRadiusTiles (80); demotion
/// crosses LocalExitRadiusTiles (96). Promote candidates are positions newly inside radius 80 of the
/// new reference; demote candidates are positions newly outside radius 96 of the old one. The
/// symmetric difference of a single box pair is wrong: an entity at distance 85 sits inside both
/// radius-96 boxes and can still cross 80 as the player approaches, and that shortcut would never
/// promote it. Proof the two edges are complete: a non-Local entity always has distance &gt; 80 from
/// the old reference (else it would be Local), so if it is now &lt;= 80 its position is in
/// box(new, 80) \ box(old, 80); a Local entity always has distance &lt;= 96 from the old reference, so
/// if it is now &gt; 96 its position is in box(old, 96) \ box(new, 96).
/// </para>
/// <para>
/// <b>The player</b> is pinned Local once and never recomputed -- including while off the map. While
/// the player is off the map, every other entity's tier keeps being computed against the player's
/// last on-map position, which is where the player returns to.
/// </para>
/// </remarks>
public sealed class ProcessingTierSystem : ISystem
{
    /// <summary>1: this runs every frame, but its per-frame work is proportional to what changed (moves and the player's edge walk), not to population, so there is nothing to stripe.</summary>
    public byte StripeCount => 1;

    private readonly DirectComponentPool<TransformComponent> _transforms;
    private readonly IMapQuery _mapQuery;
    private readonly FrameEventBuffer<EntityMovedEvent> _movedEntities;
    private readonly ProcessingTierResolver _resolver;
    private readonly IPlayerQuery? _playerQuery;

    /// <summary>Set the first time the player is observed on the map. The player is normally already pinned by the spawn sequence (FloorBuilder.CreatePlayer); this is the fallback for any path that did not, and it runs once, never per frame.</summary>
    private bool _playerPinned;

    public ProcessingTierSystem(
        DirectComponentPool<TransformComponent> transforms,
        IMapQuery mapQuery,
        FrameEventBuffer<EntityMovedEvent> movedEntities,
        ProcessingTierResolver resolver,
        IPlayerQuery? playerQuery)
    {
        _transforms = transforms;
        _mapQuery = mapQuery;
        _movedEntities = movedEntities;
        _resolver = resolver;
        _playerQuery = playerQuery;
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        if (_playerQuery is null)
        {
            return;
        }

        var playerEntityId = _playerQuery.PlayerEntityId;

        if (_transforms.TryGetReadonly(playerEntityId, out var playerTransform))
        {
            if (!_playerPinned)
            {
                _resolver.PinLocalAndNotify(playerEntityId);
                _playerPinned = true;
            }

            var livePosition = playerTransform.Position;

            if (_resolver.ReferencePosition is not { } reference)
            {
                // Nothing set a reference ahead of population, so nothing was tiered at creation.
                // Correct but expensive: the spawn sequence normally sets the reference first so
                // this never runs.
                _resolver.SetReferencePosition(livePosition);
                FullRebuild();
            }
            else if (livePosition != reference)
            {
                _resolver.SetReferencePosition(livePosition);
                OnReferenceMoved(reference, livePosition);
            }
        }

        // No live player position: keep computing against the retained last on-map position.
        // Before the player has ever been placed there is nothing to compute against at all.
        if (_resolver.ReferencePosition is null)
        {
            return;
        }

        foreach (var moved in _movedEntities.Items)
        {
            _resolver.Retier(moved.EntityId);
        }
    }

    /// <summary>
    /// The reference moved from oldReference to newReference. A MapLayer change or a Neighborhood/
    /// Borough cell crossing reclassifies every entity, so it rebuilds; otherwise only the Local
    /// edges can have changed. See this class's own remarks for why there are two edges.
    /// </summary>
    private void OnReferenceMoved(Vector3Int oldReference, Vector3Int newReference)
    {
        if (oldReference.Z != newReference.Z ||
            !ProcessingTierResolver.SameCell(oldReference, newReference, ProcessingTierResolver.NeighborhoodSizeTiles) ||
            !ProcessingTierResolver.SameCell(oldReference, newReference, ProcessingTierResolver.BoroughSizeTiles))
        {
            FullRebuild();
            return;
        }

        var z = newReference.Z;

        // Promote candidates: newly inside the entry radius.
        RetierDifference(SquareAround(newReference, ProcessingTierResolver.LocalRadiusTiles), SquareAround(oldReference, ProcessingTierResolver.LocalRadiusTiles), z);

        // Demote candidates: newly outside the exit radius.
        RetierDifference(SquareAround(oldReference, ProcessingTierResolver.LocalExitRadiusTiles), SquareAround(newReference, ProcessingTierResolver.LocalExitRadiusTiles), z);
    }

    /// <summary>Every positioned entity, recomputed. Rare -- a MapLayer change, a cell crossing, or a missing reference -- and O(capacity) when it happens.</summary>
    private void FullRebuild()
    {
        for (var entityId = 0; entityId < _transforms.Capacity; entityId++)
        {
            if (_transforms.Has(entityId))
            {
                _resolver.Retier(entityId);
            }
        }
    }

    /// <summary>Inclusive tile rectangle of Chebyshev radius around center.</summary>
    private static (int MinX, int MinY, int MaxX, int MaxY) SquareAround(Vector3Int center, int radius) =>
        (center.X - radius, center.Y - radius, center.X + radius, center.Y + radius);

    /// <summary>
    /// Retiers every entity on tiles in a \ b, on layer z. For two equal squares offset by (dx, dy)
    /// the difference is at most two strips -- the columns of a outside b's x-range, then the rows of
    /// a inside b's x-range but outside its y-range -- so the cost is proportional to how far the
    /// reference moved, not to the square's area. If the squares do not overlap at all, the first
    /// strip is all of a.
    /// </summary>
    private void RetierDifference((int MinX, int MinY, int MaxX, int MaxY) a, (int MinX, int MinY, int MaxX, int MaxY) b, int z)
    {
        if (a.MinX < b.MinX)
        {
            RetierRectangle(a.MinX, a.MinY, System.Math.Min(a.MaxX, b.MinX - 1), a.MaxY, z);
        }

        if (a.MaxX > b.MaxX)
        {
            RetierRectangle(System.Math.Max(a.MinX, b.MaxX + 1), a.MinY, a.MaxX, a.MaxY, z);
        }

        var overlapMinX = System.Math.Max(a.MinX, b.MinX);
        var overlapMaxX = System.Math.Min(a.MaxX, b.MaxX);
        if (overlapMinX > overlapMaxX)
        {
            return;
        }

        if (a.MinY < b.MinY)
        {
            RetierRectangle(overlapMinX, a.MinY, overlapMaxX, System.Math.Min(a.MaxY, b.MinY - 1), z);
        }

        if (a.MaxY > b.MaxY)
        {
            RetierRectangle(overlapMinX, System.Math.Max(a.MinY, b.MaxY + 1), overlapMaxX, a.MaxY, z);
        }
    }

    /// <summary>Retiers every entity found on tiles in the inclusive rectangle, clamped to the map.</summary>
    private void RetierRectangle(int minX, int minY, int maxX, int maxY, int z)
    {
        var size = _mapQuery.MapSize;
        minX = System.Math.Max(minX, 0);
        minY = System.Math.Max(minY, 0);
        maxX = System.Math.Min(maxX, size.X - 1);
        maxY = System.Math.Min(maxY, size.Y - 1);

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                RetierTile(new Vector3Int(x, y, z));
            }
        }
    }

    /// <summary>
    /// Retiers everything the map indexes at this tile: the Blocking occupant, every non-Blocking
    /// occupant, and the terrain for this layer. A multi-tile entity is found on every tile it
    /// covers and so may be retiered more than once -- harmless, since Retier is idempotent and
    /// computes from the entity's own origin Position regardless of which tile found it.
    /// </summary>
    private void RetierTile(Vector3Int tile)
    {
        var blocking = _mapQuery.GetEntityIdAt(tile);
        if (blocking >= 0)
        {
            _resolver.Retier(blocking);
        }

        foreach (var occupant in _mapQuery.GetOccupantEntityIdsAt(tile))
        {
            _resolver.Retier(occupant);
        }

        var terrain = _mapQuery.GetTerrainEntityIdAt(tile);
        if (terrain >= 0)
        {
            _resolver.Retier(terrain);
        }
    }
}
