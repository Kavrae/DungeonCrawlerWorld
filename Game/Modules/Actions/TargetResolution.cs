using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Math;
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
/// the aimed tile once it has been destroyed, or the caster's own for a Self shape; clamped, for a
/// SingleTarget or Burst whose target has moved out of range, to the farthest tile in range on the
/// line toward it. A dead marked entity is still there, so its corpse's tile is where it lands. An
/// activation whose spec is GroundOnly ignores any mark.
/// </para>
/// </remarks>
public sealed class TargetResolution(
    IMapQuery map,
    DirectComponentPool<TransformComponent> transforms,
    EntityKeys entityKeys,
    PackedComponentPool<DeadComponent> deadEntities,
    MultiComponentPool<NonBlockingComponent> nonBlocking,
    PackedComponentPool<AbilityScoresComponent> abilityScores)
{
    private const int MarkingCandidateBufferLength = 16;

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

        if (IsSelfOnly(spec.Shape))
        {
            return Mark(casterEntityId, spec, aimedTile, casterEntityId);
        }

        return PickMarked(aimedTile, now) is { } markedEntityId
            ? Mark(casterEntityId, spec, aimedTile, markedEntityId)
            : new TargetSelection(TargetingMode.Target, aimedTile, EntityKey.None, default, TargetSelection.ClampToUShort(spec.Range), TargetSelection.ClampToUShort(spec.AreaSize));
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
            centre = ClampToRange(casterTransform.Position, markedTile, spec, selection.Range);
            reachedMarked = centre == markedTile;
        }

        if (IsSelfOnly(spec.Shape))
        {
            centre = casterTransform.Position;
            reachedMarked = markedEntityId is not null;
        }

        if (markedEntityId is not null && spec.TargetModeAffects == TargetModeAffects.MarkedOnly)
        {
            tiles.Add(centre);
            return new ResolvedTargets(centre, reachedMarked ? markedEntityId : null, MarkedOnly: true);
        }

        TargetShapeResolver.Resolve(spec.Shape, casterTransform.Position, casterTransform.Size, centre, selection.Range, selection.AreaSize, map.Bounds, tiles, spec.Metric);
        return new ResolvedTargets(centre, markedEntityId, MarkedOnly: false);
    }

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

        return new TargetSelection(TargetingMode.Target, aimedTile, entityKeys.GetKey(markedEntityId), offset, TargetSelection.ClampToUShort(spec.Range), TargetSelection.ClampToUShort(spec.AreaSize));
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

    /// <summary>target, or -- for a SingleTarget or Burst whose target is past range -- the farthest tile in range on the line from origin toward it. Other shapes take a direction, not an anchor, so they are not clamped.</summary>
    private static Vector3Int ClampToRange(Vector3Int origin, Vector3Int target, TargetingSpec spec, int range)
    {
        var anchorsOnTarget = (spec.Shape & (TargetShape.SingleTarget | TargetShape.Burst)) != 0;
        var metric = (spec.Shape & TargetShape.SingleTarget) != 0 ? spec.Metric : DistanceMetric.Manhattan;
        if (!anchorsOnTarget || DistanceOf(origin, target, metric) <= range)
        {
            return target;
        }

        var lastInRange = new Vector3Int(origin.X, origin.Y, target.Z);
        var deltaX = System.Math.Abs(target.X - origin.X);
        var negativeDeltaY = -System.Math.Abs(target.Y - origin.Y);
        var stepX = System.Math.Sign(target.X - origin.X);
        var stepY = System.Math.Sign(target.Y - origin.Y);
        var error = deltaX + negativeDeltaY;
        var x = origin.X;
        var y = origin.Y;

        while (x != target.X || y != target.Y)
        {
            var doubledError = 2 * error;
            if (doubledError >= negativeDeltaY)
            {
                error += negativeDeltaY;
                x += stepX;
            }

            if (doubledError <= deltaX)
            {
                error += deltaX;
                y += stepY;
            }

            var step = new Vector3Int(x, y, target.Z);
            if (DistanceOf(origin, step, metric) > range)
            {
                break;
            }

            lastInRange = step;
        }

        return lastInRange;
    }

    private static int DistanceOf(Vector3Int origin, Vector3Int tile, DistanceMetric metric) =>
        metric == DistanceMetric.Chebyshev ? GridDistance.ChebyshevDistance(origin, tile) : GridDistance.ManhattanDistance(origin, tile);

    /// <summary>A Self shape alone: the caster is the target, whatever was aimed at.</summary>
    private static bool IsSelfOnly(TargetShape shape) => shape == TargetShape.Self;

    /// <summary>A roll that is the same for the same tile and frame.</summary>
    private static ulong Roll(Vector3Int tile, long now)
    {
        var mixed = unchecked(((ulong)(uint)tile.X * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)tile.Y * 0xC2B2AE3D27D4EB4FUL) ^ ((ulong)(uint)tile.Z * 0x165667B19E3779F9UL) ^ ((ulong)now * 0x27D4EB2F165667C5UL));
        mixed = unchecked((mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL);
        mixed = unchecked((mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL);
        return mixed ^ (mixed >> 31);
    }
}
