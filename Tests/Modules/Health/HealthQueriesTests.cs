using Engine.ECS.Components.Stores;
using Game.Modules.Health;
using Game.Modules.Health.Components;

namespace Tests.Modules.Health;

[TestClass]
public sealed class HealthQueriesTests
{
    private static PackedComponentPool<SimpleHealthComponent> CreateSimplePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static MultiComponentPool<BodyPartComponent> CreateBodyPartsPool() =>
        new(maximumEntityCount: 10, initialCapacity: 8);

    [TestMethod]
    public void TryGetTotals_BodyPartsOnly_SumsAcrossEveryPart()
    {
        var simpleHealth = CreateSimplePool();
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Head", BodyPartType.Head, currentHealth: 9, maximumHealth: 10, isVital: true));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, currentHealth: 15, maximumHealth: 20, isVital: true));
        bodyParts.Add(0, new BodyPartComponent("Arm", BodyPartType.Arm, currentHealth: 10, maximumHealth: 15, isVital: false));

        var found = HealthQueries.TryGetTotals(simpleHealth, bodyParts, 0, out var current, out var maximum);

        Assert.IsTrue(found);
        Assert.AreEqual(34f, current);
        Assert.AreEqual(45f, maximum);
    }

    [TestMethod]
    public void TryGetTotals_SimpleHealthPresent_FallsThroughToSimpleHealth()
    {
        var simpleHealth = CreateSimplePool();
        var bodyParts = CreateBodyPartsPool();
        simpleHealth.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));

        var found = HealthQueries.TryGetTotals(simpleHealth, bodyParts, 0, out var current, out var maximum);

        Assert.IsTrue(found);
        Assert.AreEqual(50f, current);
        Assert.AreEqual(100f, maximum);
    }

    [TestMethod]
    public void TryGetTotals_NeitherPoolHasEntity_ReturnsFalse()
    {
        var simpleHealth = CreateSimplePool();
        var bodyParts = CreateBodyPartsPool();

        var found = HealthQueries.TryGetTotals(simpleHealth, bodyParts, 0, out var current, out var maximum);

        Assert.IsFalse(found);
        Assert.AreEqual(0f, current);
        Assert.AreEqual(0f, maximum);
    }
}
