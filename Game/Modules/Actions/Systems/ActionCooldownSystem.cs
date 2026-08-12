using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Actions.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Game.Modules.Actions.Systems;

/// <summary>
/// Passively counts every ActionInstanceComponent's CooldownFramesRemaining down toward 0,
/// independent of the shared ActionLock (see ActionLockSystem for that one) -- applies to any
/// action with a cooldown regardless of ActionTimingCategory.
/// </summary>
public sealed class ActionCooldownSystem : ISystem
{
    private const byte StripeCountValue = 10;

    public byte StripeCount => StripeCountValue;

    private readonly MultiComponentPool<ActionInstanceComponent> _actionInstances;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public ActionCooldownSystem(MultiComponentPool<ActionInstanceComponent> actionInstances, DirectComponentPool<ProcessingTierComponent> processingTiers, ProcessingTierEvents processingTierEvents)
    {
        _actionInstances = actionInstances;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, actionInstances, processingTiers, processingTierEvents);
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _tieredStripeSet.GetDueEntities(time.FrameCount))
        {
            for (var denseIndex = _actionInstances.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _actionInstances.GetNextDenseIndex(denseIndex))
            {
                if (_actionInstances.GetReadonlyByDenseIndex(denseIndex).CooldownFramesRemaining > 0)
                {
                    _actionInstances.UpdateByDenseIndex(denseIndex, static (ref ActionInstanceComponent instance) =>
                    {
                        instance.CooldownFramesRemaining = (short)Math.Max(0, instance.CooldownFramesRemaining - StripeCountValue);
                    });
                }
            }
        }
    }
}
