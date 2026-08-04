using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Abilities.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Game.Modules.Abilities.Systems;

/// <summary>
/// Passively counts every AbilityInstanceComponent's CooldownFramesRemaining down toward 0,
/// independent of the shared ActionLock (see ActionLockSystem for that one) -- applies to any
/// ability with a cooldown regardless of ActionTimingCategory.
/// </summary>
public sealed class AbilityCooldownSystem : ISystem
{
    private const byte StripeCountValue = 10;

    public byte StripeCount => StripeCountValue;

    private readonly MultiComponentPool<AbilityInstanceComponent> _abilityInstances;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public AbilityCooldownSystem(MultiComponentPool<AbilityInstanceComponent> abilityInstances, DirectComponentPool<ProcessingTierComponent> processingTiers, ProcessingTierEvents processingTierEvents)
    {
        _abilityInstances = abilityInstances;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, abilityInstances, processingTiers, processingTierEvents);
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _tieredStripeSet.GetDueEntities(time.FrameCount))
        {
            for (var denseIndex = _abilityInstances.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _abilityInstances.GetNextDenseIndex(denseIndex))
            {
                if (_abilityInstances.GetReadonlyByDenseIndex(denseIndex).CooldownFramesRemaining > 0)
                {
                    _abilityInstances.UpdateByDenseIndex(denseIndex, static (ref AbilityInstanceComponent instance) =>
                    {
                        instance.CooldownFramesRemaining = (short)Math.Max(0, instance.CooldownFramesRemaining - StripeCountValue);
                    });
                }
            }
        }
    }
}
