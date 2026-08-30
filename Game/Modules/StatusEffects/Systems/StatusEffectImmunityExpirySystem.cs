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
public sealed class StatusEffectImmunityExpirySystem : ISystem
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

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _tieredStripeSet.GetDueEntities(time.FrameCount))
        {
            for (var denseIndex = _immunities.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _immunities.GetNextDenseIndex(denseIndex))
            {
                ref readonly var immunity = ref _immunities.GetReadonlyByDenseIndex(denseIndex);
                if (immunity.RemainingDurationFrames > 0)
                {
                    _immunities.UpdateByDenseIndex(denseIndex, static (ref StatusEffectImmunityComponent immunity) => immunity.RemainingDurationFrames--);
                }
            }

            while (_immunities.RemoveFirst(entityId, static (ref readonly StatusEffectImmunityComponent immunity) => immunity.RemainingDurationFrames == 0))
            {
            }
        }
    }
}
