using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Actions.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Game.Modules.Actions.Systems;

/// <summary>Manages the active cooldowns for each entity's actions.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ActionCooldownSystem : ITieredSystem
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

    public TieredEntityStripeSet Tiers => _tieredStripeSet;

    /// <summary>Runs every tier -- see ITieredSystem's own remarks. SystemManager does not call this.</summary>
    public void Update(EngineTime time, byte stripeIndex) => TieredSystemRunner.Run(this, time);

    /// <summary>
    /// Reduces each due entity's cooldowns by framesPerVisit, not the base StripeCountValue -- a
    /// coarse-tier entity is only visited every StripeCount * divisor frames. Decrementing by
    /// StripeCountValue regardless of tier (as this used to) meant a Beyond-tier entity's
    /// cooldowns ran at an eighth of real time.
    /// </summary>
    public void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit)
    {
        foreach (var entityId in entityIds)
        {
            for (var denseIndex = _actionInstances.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _actionInstances.GetNextDenseIndex(denseIndex))
            {
                if (_actionInstances.GetReadonlyByDenseIndex(denseIndex).CooldownFramesRemaining > 0)
                {
                    _actionInstances.UpdateByDenseIndex(denseIndex, framesPerVisit, static (ref ActionInstanceComponent instance, ushort frames) =>
                    {
                        instance.CooldownFramesRemaining = MathUtility.DecrementClamped(instance.CooldownFramesRemaining, frames);
                    });
                }
            }
        }
    }
}
