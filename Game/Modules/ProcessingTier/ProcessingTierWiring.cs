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
///
/// An entity with no ProcessingTierComponent yet fails open to Beyond (the cheapest, least-
/// frequently-visited tier), not Local -- see ProcessingTierSystem's own doc comment for why:
/// bulk population creates thousands of entities before ProcessingTierSystem has ever run, and
/// assuming "unknown = might be right next to the player" for all of them at once is far more
/// expensive than the bounded, self-correcting staleness of assuming "unknown = probably far,
/// promote it once its real tier lands."
/// </summary>
public static class ProcessingTierWiring
{
    public static TieredEntityStripeSet CreateAndWire(byte baseStripeCount, IEntityMembershipPool drivingPool, DirectComponentPool<ProcessingTierComponent> processingTiers, ProcessingTierEvents processingTierEvents)
    {
        var tieredStripeSet = new TieredEntityStripeSet(baseStripeCount, ProcessingTierDivisors.ByTierIndex, drivingPool.EntityIds,
            entityId => processingTiers.TryGetReadonly(entityId, out var c) ? (byte)c.Tier : (byte)ProcessingTierLevel.Beyond);
        drivingPool.EntityAdded += tieredStripeSet.OnMemberAdded;
        drivingPool.EntityRemoved += tieredStripeSet.OnMemberRemoved;
        processingTierEvents.TierChanged += (entityId, tier) => tieredStripeSet.OnEntityTierChanged(entityId, (byte)tier);

        return tieredStripeSet;
    }
}
