using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Game.Modules.Core.Systems;

/// <summary>Passively counts every entity's shared action lock down toward 0, once per stripe cycle.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ActionLockSystem : ISystem
{
    private const byte StripeCountValue = 10;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public ActionLockSystem(PackedComponentPool<ActionLockComponent> actionLocks, DirectComponentPool<ProcessingTierComponent> processingTiers, ProcessingTierEvents processingTierEvents)
    {
        _actionLocks = actionLocks;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, actionLocks, processingTiers, processingTierEvents);
    }

    /// <summary>Decrements the lock frames remaining for each due entity.</summary>
    /// <remarks>CurrentLockFramesRemaining is decremented by StripeCountValue to account for the number of ticks between updates for this entity stripe.</remarks>
    /// <param name="time"></param>
    /// <param name="stripeIndex"></param>
    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _tieredStripeSet.GetDueEntities(time.FrameCount))
        {
            if (_actionLocks.TryGetReadonly(entityId, out var actionLock) && actionLock.CurrentLockFramesRemaining != 0)
            {
                _actionLocks.TryUpdate(entityId, static (ref ActionLockComponent actionLockComponent) =>
                {
                    actionLockComponent.CurrentLockFramesRemaining = MathUtility.DecrementClamped(actionLockComponent.CurrentLockFramesRemaining, StripeCountValue);
                });
            }
        }
    }
}
