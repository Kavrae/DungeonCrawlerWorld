using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatusEffects.Components;

namespace Game.Modules.StatusEffects.Systems;

/// <summary>
/// Ticks every active StatusEffectImmunityComponent's RemainingDurationFrames down toward 0 and
/// removes it once it gets there -- a permanent immunity (RemainingDurationFrames == null) never
/// enters the decrement branch and never equals 0, so it's untouched forever. Mirrors
/// StatModifierExpirySystem's own two-pass shape (RemoveFirst/RemoveByDenseIndex compact the
/// whole pool's dense array, which would corrupt an in-progress GetNextDenseIndex walk if a
/// removal happened mid-walk), but driven directly off StatusEffectImmunityComponent itself --
/// unlike StatModifierComponent (where most of the population holds only permanent modifiers,
/// motivating ExpiringStatModifierComponent's separate marker), immunity is expected to be rare
/// enough that visiting every immunity-holding entity directly is fine, the same reasoning
/// ComplexHealthRegenSystem/BurningSystem already apply to their own pools.
/// </summary>
public sealed class StatusEffectImmunityExpirySystem : ITieredSystem
{
    private const byte StripeCountValue = 1;

    public byte StripeCount => StripeCountValue;

    private readonly MultiComponentPool<StatusEffectImmunityComponent> _immunities;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public StatusEffectImmunityExpirySystem(
        MultiComponentPool<StatusEffectImmunityComponent> immunities,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents)
    {
        _immunities = immunities;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, immunities, processingTiers, processingTierEvents);
    }

    /// <summary>
    /// Driven per tier by TieredSystemRunner, which hands UpdateBucket that tier's actual
    /// frames-per-visit, so the duration is reduced by the span the entity was really absent for. This used to decrement by exactly 1 per
    /// visit regardless of tier -- correct only at Local, where base StripeCount 1 means one visit
    /// per frame. A coarse-tier entity is visited every StripeCount * divisor frames and lost a
    /// single frame of immunity each time, so its immunity outlasted its authored duration by that
    /// divisor. Same correction as StatModifierExpirySystem, which had the identical defect.
    /// </summary>
    public void Update(EngineTime time, byte stripeIndex) => TieredSystemRunner.Run(this, time);

    public TieredEntityStripeSet Tiers => _tieredStripeSet;

    /// <summary>One tier's due entities, scaled by that tier's framesPerVisit -- see ITieredSystem.UpdateBucket.</summary>
    public void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit)
    {
        foreach (var entityId in entityIds)
        {
            for (var denseIndex = _immunities.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _immunities.GetNextDenseIndex(denseIndex))
            {
                ref readonly var immunity = ref _immunities.GetReadonlyByDenseIndex(denseIndex);
                if (immunity.RemainingDurationFrames > 0)
                {
                    _immunities.UpdateByDenseIndex(denseIndex, framesPerVisit, static (ref StatusEffectImmunityComponent immunity, ushort frames) =>
                        immunity.RemainingDurationFrames = (ushort)System.Math.Max(0, immunity.RemainingDurationFrames!.Value - frames));
                }
            }

            while (_immunities.RemoveFirst(entityId, static (ref readonly StatusEffectImmunityComponent immunity) => immunity.RemainingDurationFrames == 0))
            {
            }
        }
    }
}
