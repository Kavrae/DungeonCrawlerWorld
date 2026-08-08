using Engine.ECS.Components.Stores;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Tests.Modules.ProcessingTier;

[TestClass]
public sealed class ProcessingTierWiringTests
{
    private readonly record struct TestMarkerComponent;

    /// <summary>
    /// An entity with no ProcessingTierComponent yet must fail open to Beyond (cheapest, least
    /// frequently visited), not Local (most expensive, visited every cycle) -- see
    /// ProcessingTierWiring's own doc comment. Verified directly against CreateAndWire's return
    /// value rather than through any one consumer system, since every TieredEntityStripeSet
    /// consumer shares this exact wiring.
    /// </summary>
    [TestMethod]
    public void CreateAndWire_EntityWithNoProcessingTierComponent_StartsInBeyondBucket_NotLocal()
    {
        var drivingPool = new MultiComponentPool<TestMarkerComponent>(maximumEntityCount: 10, initialCapacity: 4);
        drivingPool.Add(0, new TestMarkerComponent());
        var processingTiers = new DirectComponentPool<ProcessingTierComponent>(10, static (ref existing, incoming) => existing = incoming);

        var tieredStripeSet = ProcessingTierWiring.CreateAndWire(baseStripeCount: 1, drivingPool, processingTiers, new ProcessingTierEvents());

        var localBucket = tieredStripeSet.GetTierBucket((int)ProcessingTierLevel.Local, frameCount: 0);
        var beyondBucket = tieredStripeSet.GetTierBucket((int)ProcessingTierLevel.Beyond, frameCount: 0);

        CollectionAssert.DoesNotContain(localBucket.ToArray(), 0);
        CollectionAssert.Contains(beyondBucket.ToArray(), 0);
    }
}
