using System.Runtime.InteropServices;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Math;
using Engine.Tiles;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions.Activators;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.World;

namespace Game.Modules.Actions;

/// <summary>Turns what a caster aimed at (TargetSelection) into the tiles an activation lands on -- the one function behind activations, windups, NPCs, Presentation's preview and the windup telegraph, so what is drawn is what lands.</summary>
/// <remarks>
/// <para>
/// Select builds a selection when an activation is confirmed: the effective range and area (a
/// scroll's scaled by the caster's Intelligence, ScrollScalingEffects), and in Target mode the entity
/// marked on the aimed tile -- a Blocking one, else a Tiny one (picked by a roll seeded from the tile
/// and frame, so the same input picks the same one), else a Phasing one. Dead entities are never
/// marked. With nothing to mark, a Target selection resolves as Ground.
/// </para>
/// <para>
/// Resolve lands it: on the marked entity's current tile (the footprint tile that was aimed at), or
/// the aimed tile once it has been destroyed, or the caster's own for a Self shape anchored on the caster; clamped, for a
/// shape anchored where its path ends whose target has moved outside the range band, to the nearest tile in the band on the
/// line toward it. A dead marked entity is still there, so its corpse's tile is where it lands. An
/// activation whose spec is GroundOnly ignores any mark.
/// </para>
/// </remarks>
public sealed class TargetResolution(
    IMapQuery map,
    ISightBlockers walls,
    DirectComponentPool<TransformComponent> transforms,
    EntityKeys entityKeys,
    PackedComponentPool<DeadComponent> deadEntities,
    MultiComponentPool<NonBlockingComponent> nonBlocking,
    PackedComponentPool<AbilityScoresComponent> abilityScores) : IPathOccupants
{
    private const int MarkingCandidateBufferLength = 16;

    /// <summary>The tiles the last traced path passed through; ResolvedTargets.PathTiles hands it out until the next Resolve.</summary>
    private readonly List<Vector3Int> _pathTiles = [];

    /// <summary>The entities the last traced path met, in order; ResolvedTargets.PathHitEntityIds hands it out until the next Resolve.</summary>
    private readonly List<int> _pathHitEntityIds = [];

    /// <summary>The caster of the path being traced, which never meets itself (IPathOccupants.EntityMetAt).</summary>
    private int _pathCasterEntityId = -1;

    /// <summary>The walls' ISightBlockers.Version -- in a game, World.SightVersion, which also counts neighborhoods loading and unloading. Besides its arguments, ResolveReachable reads only the walls and the map's edge, so its tiles can be kept until this or the arguments change.</summary>
    public int SightVersion => walls.Version;

    /// <summary>The caster's targeting for activator as it applies now: a scroll's range and area scaled by the caster's Intelligence, anything else's as declared.</summary>
    public TargetingSpec EffectiveSpec(int casterEntityId, IActionActivator activator)
    {
        if (activator is not ScrollActivator)
        {
            return activator.Targeting;
        }

        var multiplier = AbilityScoreQueries.TryGetComponent(abilityScores, casterEntityId, AbilityScoreType.Intelligence, out var intelligence)
            ? ScrollScalingEffects.ComputeScaleMultiplier(intelligence.Total)
            : 1.0f;
        return ScrollScalingEffects.ScaleTargeting(activator.Targeting, multiplier);
    }

    /// <summary>The selection for casterEntityId activating activator at aimedTile in mode, marking whoever stands there if Target mode applies.</summary>
    /// <param name="now">Seeds the pick among several Tiny entities on the tile.</param>
    public TargetSelection Select(int casterEntityId, IActionActivator activator, TargetingMode mode, Vector3Int aimedTile, long now)
    {
        var spec = EffectiveSpec(casterEntityId, activator);
        if (mode == TargetingMode.Ground || spec.Modes == TargetingModes.GroundOnly)
        {
            return TargetSelection.Ground(aimedTile, spec);
        }

        if (spec.IsSelfOnly)
        {
            return Mark(casterEntityId, spec, aimedTile, casterEntityId);
        }

        return PickMarked(aimedTile, now) is { } markedEntityId
            ? Mark(casterEntityId, spec, aimedTile, markedEntityId)
            : new TargetSelection(TargetingMode.Target, aimedTile, EntityKey.None, default, TargetSelection.ClampToUShort(spec.MaximumRange), TargetSelection.ClampToUShort(spec.AreaSize));
    }

    /// <summary>A Target selection marking targetEntityId where it stands -- an NPC's choice, or a potion used on oneself. Ground at its tile when activator offers no Target mode.</summary>
    public TargetSelection SelectEntity(int casterEntityId, IActionActivator activator, int targetEntityId)
    {
        var spec = EffectiveSpec(casterEntityId, activator);
        var position = transforms.TryGetReadonly(targetEntityId, out var transform) ? transform.Position : default;

        return spec.Modes == TargetingModes.GroundOnly
            ? TargetSelection.Ground(position, spec)
            : Mark(casterEntityId, spec, position, targetEntityId);
    }

    /// <summary>Fills tiles with where selection lands for casterEntityId under spec's shape, metric and modes (its range and area are the selection's), and says what it centred on and reached.</summary>
    /// <remarks>tiles is cleared first. For a MarkedOnly activation it holds the one tile the marked entity was reached on, for drawing; what is affected is MarkedEntityId alone.</remarks>
    public ResolvedTargets Resolve(int casterEntityId, TargetingSpec spec, TargetSelection selection, List<Vector3Int> tiles)
    {
        tiles.Clear();
        if (!transforms.TryGetReadonly(casterEntityId, out var casterTransform))
        {
            return new ResolvedTargets(selection.AimedTile, null, false);
        }

        var centre = selection.AimedTile;
        int? markedEntityId = null;
        var reachedMarked = false;

        if (spec.Modes != TargetingModes.GroundOnly && selection.HasMarkedEntity && TryLocateMarked(selection, out var markedId, out var markedTile))
        {
            markedEntityId = markedId;
            centre = ClampToRangeBand(casterTransform.Position, casterTransform.Size, markedTile, spec, RangesOf(spec, selection));
            reachedMarked = centre == markedTile;
        }

        if (spec.IsSelfOnly)
        {
            centre = casterTransform.Position;
            reachedMarked = markedEntityId is not null;
        }

        if (markedEntityId is not null && spec.TargetModeAffects == TargetModeAffects.MarkedOnly)
        {
            tiles.Add(centre);
            return new ResolvedTargets(centre, reachedMarked ? markedEntityId : null, MarkedOnly: true);
        }

        var ranges = RangesOf(spec, selection);
        if (spec.Path == TargetPath.Placed)
        {
            TargetShapeResolver.Resolve(spec.Shape, spec.Anchor, casterTransform.Position, casterTransform.Size, centre, ranges, map.Bounds, tiles, LineOfSightOf(spec));
            return new ResolvedTargets(centre, markedEntityId, MarkedOnly: false);
        }

        _pathCasterEntityId = casterEntityId;
        var request = new TargetPathRequest(spec.Path, spec.PathEnd, spec.ProjectileHitCount, spec.PathWalls, spec.WallBounceCount, casterTransform.Position, casterTransform.Size, centre, ranges);
        if (!TargetShapeResolver.TryResolveAlongPath(spec.Shape, spec.Anchor, in request, map.Bounds, this, _pathTiles, _pathHitEntityIds, tiles, out var trace, LineOfSightOf(spec)))
        {
            return new ResolvedTargets(centre, null, MarkedOnly: false);
        }

        var pathReachedMarked = markedEntityId is { } marked && (trace.EndTile == centre || _pathHitEntityIds.Contains(marked));
        return new ResolvedTargets(spec.Anchor == TargetAnchor.PathEnd ? trace.EndTile : centre, pathReachedMarked ? markedEntityId : null, MarkedOnly: false)
        {
            PathHitEntityIds = _pathHitEntityIds,
            PathTiles = _pathTiles,
        };
    }

    /// <inheritdoc cref="IPathOccupants.EntityMetAt"/>
    /// <remarks>A living Blocking entity other than the caster whose path is being traced. Tiny and Phasing entities are too hard to hit with a projectile: an area is how to reach them.</remarks>
    int IPathOccupants.EntityMetAt(Vector3Int tile)
    {
        var entityId = map.GetEntityIdAt(tile);
        return entityId == -1 || entityId == _pathCasterEntityId || deadEntities.Has(entityId) ? -1 : entityId;
    }

    /// <summary>
    /// Fills aimableTiles with every tile spec can be aimed at from a caster whose footprint is at origin, whatever the aim --
    /// the range band, with spec's aim rule only what the caster can see, and with a path that walls stop or turn only what
    /// its first leg reaches (TargetShapeResolver.ResolveReachable) -- and reachedTiles with those and every tile a Projectile
    /// aimed at one of them could hit on its way (PathTracer.AppendTilesPassed).
    /// </summary>
    /// <remarks>
    /// A path follows the line sight sees its aim along (PathTracer), and every tile on that line is one sight reaches too,
    /// so the two differ only for a Projectile that bounces and flies on past its aim: its later legs turn into tiles that
    /// can't be aimed at directly. One that stops on its aim never gets past it to bounce.
    /// spec is used as given: pass the effective one (EffectiveSpec) for a scaled range.
    /// </remarks>
    public void ResolveReachable(TargetingSpec spec, Vector3Int origin, Vector2Byte originSize, List<Vector3Int> aimableTiles, List<Vector3Int> reachedTiles)
    {
        TargetShapeResolver.ResolveReachable(spec.Shape, spec.Anchor, origin, originSize, spec.Ranges, map.Bounds, aimableTiles, LineOfSightOf(spec), spec.Path, spec.PathWalls);

        reachedTiles.Clear();
        reachedTiles.AddRange(aimableTiles);
        if (spec.Path == TargetPath.Projectile && spec.PathEnd == ProjectilePathEnd.AtMaximumRange && spec.PathWalls == PathWallRule.BouncesOffWalls && spec.WallBounceCount > 0)
        {
            var request = new TargetPathRequest(spec.Path, spec.PathEnd, spec.ProjectileHitCount, spec.PathWalls, spec.WallBounceCount, origin, originSize, origin, spec.Ranges);
            PathTracer.AppendTilesPassed(in request, CollectionsMarshal.AsSpan(aimableTiles), walls, map.Bounds, reachedTiles);
        }
    }

    /// <summary>Whether casterEntityId could aim spec at tile now: a targetable tile (IsTargetableTile) in its aim's range band (TargetingSpec.AimRanges) and, with spec's aim rule, in its sight.</summary>
    public bool IsAimable(int casterEntityId, TargetingSpec spec, Vector3Int tile) =>
        IsTargetableTile(tile) &&
        transforms.TryGetReadonly(casterEntityId, out var transform) &&
        spec.AimRanges.IsInRangeBand(transform.Position, transform.Size, tile) &&
        (spec.AimLineOfSight != LineOfSightRule.Required || LineOfSight.IsVisibleFromFootprint(transform.Position, transform.Size, tile, walls));

    /// <summary>Whether anything may be aimed at or placed on tile, whoever aims and in either mode: not a wall, and on the map.</summary>
    /// <remarks>The rule every resolve applies through its walls (TargetLineOfSight.PlacementWalls), named for a caller choosing a tile: an NPC, or the player's aim falling back to a tile near a wall.</remarks>
    public bool IsTargetableTile(Vector3Int tile) => !walls.BlocksSight(tile.X, tile.Y, tile.Z);

    /// <summary>The entity Target mode marks on tile: a living Blocking one, else a Tiny one (several: picked by a roll seeded from tile and now), else a Phasing one. Null for none.</summary>
    public int? PickMarked(Vector3Int tile, long now)
    {
        var blockingEntityId = map.GetEntityIdAt(tile);
        if (blockingEntityId != -1 && !deadEntities.Has(blockingEntityId))
        {
            return blockingEntityId;
        }

        var occupantIds = map.GetOccupantEntityIdSpanAt(tile);
        Span<int> candidateBuffer = stackalloc int[MarkingCandidateBufferLength];

        foreach (var kind in (ReadOnlySpan<NonBlockingKind>)[NonBlockingKind.Tiny, NonBlockingKind.Phasing])
        {
            var candidateCount = 0;
            foreach (var occupantId in occupantIds)
            {
                if (candidateCount < candidateBuffer.Length && !deadEntities.Has(occupantId) && (NonBlockingQueries.CombinedKind(nonBlocking, occupantId) & kind) != 0)
                {
                    candidateBuffer[candidateCount++] = occupantId;
                }
            }

            if (candidateCount > 0)
            {
                return candidateBuffer[(int)(Roll(tile, now) % (ulong)candidateCount)];
            }
        }

        return null;
    }

    private TargetSelection Mark(int casterEntityId, TargetingSpec spec, Vector3Int aimedTile, int markedEntityId)
    {
        var offset = default(Vector2Byte);
        if (markedEntityId != casterEntityId && transforms.TryGetReadonly(markedEntityId, out var markedTransform))
        {
            offset = new Vector2Byte(
                (byte)System.Math.Clamp(aimedTile.X - markedTransform.Position.X, 0, markedTransform.Size.X - 1),
                (byte)System.Math.Clamp(aimedTile.Y - markedTransform.Position.Y, 0, markedTransform.Size.Y - 1));
        }

        return new TargetSelection(TargetingMode.Target, aimedTile, entityKeys.GetKey(markedEntityId), offset, TargetSelection.ClampToUShort(spec.MaximumRange), TargetSelection.ClampToUShort(spec.AreaSize));
    }

    /// <summary>The marked entity and the tile of it that was aimed at, if it still exists and is on the map.</summary>
    private bool TryLocateMarked(TargetSelection selection, out int markedEntityId, out Vector3Int markedTile)
    {
        markedTile = default;
        if (!entityKeys.TryGetEntityId(selection.MarkedEntity, out markedEntityId) || !transforms.TryGetReadonly(markedEntityId, out var transform) || !map.IsOnMap(transform.Position))
        {
            return false;
        }

        markedTile = new Vector3Int(transform.Position.X + selection.MarkedFootprintOffset.X, transform.Position.Y + selection.MarkedFootprintOffset.Y, transform.Position.Z);
        return true;
    }

    /// <summary>target, or -- for a shape anchored where its path ends, whose target is outside the range band -- the tile in the band nearest it on the line from the caster (TargetShapeResolver.ClampToRangeBand). A shape anchored on the caster takes a direction, not an anchor, so it is not clamped.</summary>
    private static Vector3Int ClampToRangeBand(Vector3Int casterPosition, Vector2Byte casterSize, Vector3Int target, TargetingSpec spec, TargetRanges ranges) =>
        spec.Anchor == TargetAnchor.PathEnd ? TargetShapeResolver.ClampToRangeBand(casterPosition, casterSize, target, ranges) : target;

    /// <summary>spec's line-of-sight rules over the map's walls.</summary>
    private TargetLineOfSight LineOfSightOf(TargetingSpec spec) => new(walls, spec.AimLineOfSight, spec.ShapeLineOfSight);

    /// <summary>The ranges a selection resolves with: its effective maximum range and area, and spec's minimum range, which is never scaled.</summary>
    private static TargetRanges RangesOf(TargetingSpec spec, TargetSelection selection) => new(selection.MaximumRange, selection.AreaSize, spec.MinimumRange);

    /// <summary>A roll that is the same for the same tile and frame.</summary>
    private static ulong Roll(Vector3Int tile, long now)
    {
        var mixed = unchecked(((ulong)(uint)tile.X * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)tile.Y * 0xC2B2AE3D27D4EB4FUL) ^ ((ulong)(uint)tile.Z * 0x165667B19E3779F9UL) ^ ((ulong)now * 0x27D4EB2F165667C5UL));
        mixed = unchecked((mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL);
        mixed = unchecked((mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL);
        return mixed ^ (mixed >> 31);
    }
}
