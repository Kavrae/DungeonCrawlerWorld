using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Actions.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Game.Modules.Actions.Systems;

/// <summary>Manages the active cooldowns for each entity's actions.</summary>
/// <cleanupVersion>1</cleanupVersion>
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

    /// <summary>Updates the cooldowns for the entities in the specified entity stripe</summary>
    /// <remarks>Cooldowns are reduced by the stripeCountValue to account for the number of ticks between updates for the updated entity stripe.</remarks>
    /// <param name="time"></param>
    /// <param name="stripeIndex"></param>
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
                        instance.CooldownFramesRemaining = MathUtility.DecrementClamped(instance.CooldownFramesRemaining, StripeCountValue);
                    });
                }
            }
        }
    }
}
