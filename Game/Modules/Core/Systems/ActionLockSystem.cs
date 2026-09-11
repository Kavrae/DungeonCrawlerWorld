using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Game.Modules.Core.Systems;

/// <summary>Passively counts every entity's shared action lock down toward 0, once per stripe cycle.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ActionLockSystem : ITieredSystem
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

    public TieredEntityStripeSet Tiers => _tieredStripeSet;

    /// <summary>Runs every tier -- see ITieredSystem's own remarks. SystemManager does not call this.</summary>
    public void Update(EngineTime time, byte stripeIndex) => TieredSystemRunner.Run(this, time);

    /// <summary>Decrements the lock frames remaining for each due entity.</summary>
    /// <remarks>
    /// By framesPerVisit, not the base StripeCountValue. This used to decrement by
    /// StripeCountValue regardless of tier, which was correct only for Local: a Beyond-tier entity
    /// is visited every StripeCount * divisor frames but had its lock reduced by StripeCount, so
    /// its action lock ticked down at a fraction of real time and it acted that many times less
    /// often than intended. That class of defect is why ITieredSystem hands framesPerVisit in as a
    /// parameter.
    /// </remarks>
    public void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit)
    {
        foreach (var entityId in entityIds)
        {
            if (_actionLocks.TryGetReadonly(entityId, out var actionLock) && actionLock.CurrentLockFramesRemaining != 0)
            {
                _actionLocks.TryUpdate(entityId, framesPerVisit, static (ref ActionLockComponent actionLockComponent, ushort frames) =>
                {
                    actionLockComponent.CurrentLockFramesRemaining = MathUtility.DecrementClamped(actionLockComponent.CurrentLockFramesRemaining, frames);
                });
            }
        }
    }
}
