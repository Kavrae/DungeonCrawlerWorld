using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.ProcessingTier.Components;

namespace Game.Modules.ProcessingTier;

/// <summary>
/// Builds a TieredEntityStripeSet already wired to drivingPool's membership and
/// processingTierEvents' TierChanged notifications -- the same four-line dance every
/// TieredEntityStripeSet-consuming system's constructor otherwise repeats by hand. Lives here
/// (not on TieredEntityStripeSet itself) because it references ProcessingTierComponent/
/// ProcessingTierLevel, which Engine.ECS.Systems.TieredEntityStripeSet deliberately never does.
/// </summary>
public static class ProcessingTierWiring
{
    public static TieredEntityStripeSet CreateAndWire(byte baseStripeCount, IEntityMembershipPool drivingPool, DirectComponentPool<ProcessingTierComponent> processingTiers, ProcessingTierEvents processingTierEvents)
    {
        var tieredStripeSet = new TieredEntityStripeSet(baseStripeCount, ProcessingTierDivisors.ByTierIndex, drivingPool.EntityIds,
            entityId => processingTiers.TryGetReadonly(entityId, out var c) ? (byte)c.Tier : (byte)ProcessingTierLevel.Local);
        drivingPool.EntityAdded += tieredStripeSet.OnMemberAdded;
        drivingPool.EntityRemoved += tieredStripeSet.OnMemberRemoved;
        processingTierEvents.TierChanged += (entityId, tier) => tieredStripeSet.OnEntityTierChanged(entityId, (byte)tier);

        return tieredStripeSet;
    }
}
