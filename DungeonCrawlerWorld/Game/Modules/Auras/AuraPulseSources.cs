using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Math;
using Engine.Tiles;
using Game.Blueprints;
using Game.Modules.Auras.Components;
using Game.Modules.Auras.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Game.Views;
using Game.World;

namespace Game.Modules.Auras;

/// <summary>The aura sources that pulse rather than sit in the field: every source on an entity that can move.</summary>
/// <remarks>
/// <para>
/// PLAN-aura-scaling.md, Decision 2: terrain and every entity source on an entity with no MovementComponent are in the
/// AuraField, written once where they stand. A source on an entity that can move -- a creature, the player, what they
/// carry -- is never in the field: AuraSystem pulses it, scanning its reach on a schedule and writing what it reaches
/// onto each target's exposure. Whether the entity is moving right now never decides it.
/// </para>
/// <para>
/// So what a tile is in is the field's value there or a pulse source's, whichever is strongest: PowerAt and
/// CollectAurasAt answer for the pulse sources, by scanning the carriers around the tile -- for inspecting a tile, never
/// per frame. Pools are fetched on first use, so a build without the Auras module never asks for them.
/// </para>
/// </remarks>
public sealed class AuraPulseSources(ComponentManager componentManager, EntityKeys entityKeys, BlueprintRegistry definitions, IOccupiedCellQuery occupiedCells, AuraCatalog auras)
{
    private MultiComponentPool<AuraSourceComponent>? _sources;
    private DirectComponentPool<TransformComponent>? _transforms;
    private PackedComponentPool<MovementComponent>? _movements;
    private PackedComponentPool<AuraAnchorComponent>? _anchors;
    private readonly HashSet<int> _countedCarriersScratch = [];

    private MultiComponentPool<AuraSourceComponent> Sources => _sources ??= componentManager.GetMultiPool<AuraSourceComponent>();

    private DirectComponentPool<TransformComponent> Transforms => _transforms ??= componentManager.GetDirectPool<TransformComponent>();

    /// <summary>The largest reach any pulse source has had this session: how far around a tile the carriers that may reach it stand.</summary>
    public int MaximumReach { get; private set; }

    /// <summary>Pulses run since the session began (a gauge).</summary>
    public long Pulses { get; private set; }

    /// <summary>Records written onto targets' exposures by pulses since the session began (a gauge).</summary>
    public long RecordsWritten { get; private set; }

    /// <summary>Whether entityId's sources pulse: it can move, and isn't an aura anchor (which stands where it was placed).</summary>
    public bool IsPulseCarrier(int entityId)
    {
        _movements ??= componentManager.GetPackedPool<MovementComponent>();
        _anchors ??= componentManager.GetPackedPool<AuraAnchorComponent>();
        return _movements.Has(entityId) && !_anchors.Has(entityId);
    }

    /// <summary>Counts a pulse source's reach towards MaximumReach.</summary>
    public void NoteReach(int reach) => MaximumReach = Math.Max(MaximumReach, reach);

    /// <summary>Counts one pulse run.</summary>
    public void CountPulse() => Pulses++;

    /// <summary>Counts one record written onto a target.</summary>
    public void CountRecord() => RecordsWritten++;

    /// <summary>Whether any pulse source reaches position.</summary>
    public bool AnyReaches(Vector3Int position)
    {
        var reaches = false;
        VisitSourcesReaching(position, (_, _, value) => reaches |= value > 0);
        return reaches;
    }

    /// <summary>The strongest value any pulse source of auraId gives position.</summary>
    public int PowerAt(Vector3Int position, byte auraId)
    {
        var strongest = 0;
        VisitSourcesReaching(position, (_, source, value) =>
        {
            if (source.AuraId == auraId)
            {
                strongest = Math.Max(strongest, value);
            }
        });

        return strongest;
    }

    /// <summary>Raises each aura's entry in strongestByAura to the strongest value a pulse source gives position, with whom it is credited to: its carrier (the strongest's, ties to the lowest key).</summary>
    public void CollectAurasAt(Vector3Int position, Dictionary<byte, (int Power, ActionSource Credit)> strongestByAura)
    {
        VisitSourcesReaching(position, (carrierId, source, value) =>
        {
            var credit = ActionSource.FromEntity(componentManager, entityKeys, carrierId, definitions);
            if (!strongestByAura.TryGetValue(source.AuraId, out var held) || value > held.Power || (value == held.Power && credit.Key.Value < held.Credit.Key.Value))
            {
                strongestByAura[source.AuraId] = (value, credit);
            }
        });
    }

    /// <summary>Adds to sources every pulse source reaching the rectangle on layer, its own and every source of each carrier standing within MaximumReach of it -- for drawing, in one scan.</summary>
    public void CollectSourcesReaching(int minX, int minY, int maxXExclusive, int maxYExclusive, int layer, List<PulseSourceReach> sources)
    {
        var reach = MaximumReach;
        if (reach == 0)
        {
            return;
        }

        _countedCarriersScratch.Clear();
        foreach (var cell in occupiedCells.GetOccupiedCellsIn(minX - reach - 2, minY - reach - 2, maxXExclusive + reach + 2, maxYExclusive + reach + 2, layer))
        {
            foreach (var carrierId in cell.Occupants)
            {
                if (!Sources.Has(carrierId) || !_countedCarriersScratch.Add(carrierId) || !IsPulseCarrier(carrierId) || !Transforms.TryGetReadonly(carrierId, out var transform))
                {
                    continue;
                }

                var center = AuraSystem.SourceTileOf(transform);
                var deltaX = Math.Max(0, Math.Max(minX - center.X, center.X - (maxXExclusive - 1)));
                var deltaY = Math.Max(0, Math.Max(minY - center.Y, center.Y - (maxYExclusive - 1)));
                for (var denseIndex = Sources.GetFirstDenseIndex(carrierId); denseIndex != -1; denseIndex = Sources.GetNextDenseIndex(denseIndex))
                {
                    var source = Sources.GetReadonlyByDenseIndex(denseIndex);
                    if (deltaX + deltaY <= source.Size)
                    {
                        sources.Add(new PulseSourceReach(source.AuraId, center, source.Power, source.Size, auras.Get(source.AuraId).Falloff));
                    }
                }
            }
        }
    }

    /// <summary>Calls visit with each pulse source reaching position and its value there, from the carriers standing within MaximumReach of it.</summary>
    private void VisitSourcesReaching(Vector3Int position, Action<int, AuraSourceComponent, int> visit)
    {
        var reach = MaximumReach;
        if (reach == 0)
        {
            return;
        }

        _countedCarriersScratch.Clear();
        foreach (var cell in occupiedCells.GetOccupiedCellsIn(position.X - reach - 2, position.Y - reach - 2, position.X + reach + 3, position.Y + reach + 3, position.Z))
        {
            foreach (var carrierId in cell.Occupants)
            {
                if (!Sources.Has(carrierId) || !_countedCarriersScratch.Add(carrierId) || !IsPulseCarrier(carrierId) || !Transforms.TryGetReadonly(carrierId, out var transform))
                {
                    continue;
                }

                var center = AuraSystem.SourceTileOf(transform);
                var distance = Math.Abs(center.X - position.X) + Math.Abs(center.Y - position.Y);
                for (var denseIndex = Sources.GetFirstDenseIndex(carrierId); denseIndex != -1; denseIndex = Sources.GetNextDenseIndex(denseIndex))
                {
                    var source = Sources.GetReadonlyByDenseIndex(denseIndex);
                    if (distance <= source.Size)
                    {
                        visit(carrierId, source, auras.Get(source.AuraId).Falloff.ValueAt(source.Power, source.Size, distance));
                    }
                }
            }
        }
    }
}
