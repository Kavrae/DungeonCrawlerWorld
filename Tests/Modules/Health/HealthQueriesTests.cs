using Engine.ECS.Components.Stores;
using Game.Modules.Health;
using Game.Modules.Health.Components;

namespace Tests.Modules.Health;

[TestClass]
public sealed class HealthQueriesTests
{
    private static PackedComponentPool<SimpleHealthComponent> CreateSimplePool() =>
        new(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static BodyPartTestWorld CreateBodyParts() =>
        new(
            new BodyPartTemplate("Head", BodyPartType.Head, 0, 10, IsVital: true),
            new BodyPartTemplate("Torso", BodyPartType.Torso, 0, 20, IsVital: true),
            new BodyPartTemplate("Arm", BodyPartType.Arm, 0, 15, IsVital: false));

    [TestMethod]
    public void TryGetTotals_BodyPartsOnly_SumsAcrossEveryPart()
    {
        var simpleHealth = CreateSimplePool();
        var world = CreateBodyParts();
        world.SetHealth(0, 0, 9);
        world.SetHealth(0, 1, 15);
        world.SetHealth(0, 2, 10);

        var found = HealthQueries.TryGetTotals(simpleHealth, world.BodyParts, 0, out var current, out var maximum);

        Assert.IsTrue(found);
        Assert.AreEqual(34f, current);
        Assert.AreEqual(45f, maximum);
    }

    /// <summary>An entity nothing has happened to holds no state component at all, and reads as every part at full health.</summary>
    [TestMethod]
    public void TryGetTotals_UndamagedEntity_ReadsAsFullWithoutAnyComponent()
    {
        var simpleHealth = CreateSimplePool();
        var world = CreateBodyParts();
        world.Give(0);

        var found = HealthQueries.TryGetTotals(simpleHealth, world.BodyParts, 0, out var current, out var maximum);

        Assert.IsTrue(found);
        Assert.AreEqual(45f, current);
        Assert.AreEqual(45f, maximum);
        Assert.IsFalse(world.States.Has(0));
    }

    [TestMethod]
    public void TryGetTotals_SimpleHealthPresent_FallsThroughToSimpleHealth()
    {
        var simpleHealth = CreateSimplePool();
        var world = CreateBodyParts();
        simpleHealth.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));

        var found = HealthQueries.TryGetTotals(simpleHealth, world.BodyParts, 0, out var current, out var maximum);

        Assert.IsTrue(found);
        Assert.AreEqual(50f, current);
        Assert.AreEqual(100f, maximum);
    }

    [TestMethod]
    public void TryGetTotals_EntityHasNeither_ReturnsFalse()
    {
        var simpleHealth = CreateSimplePool();
        var world = CreateBodyParts();

        var found = HealthQueries.TryGetTotals(simpleHealth, world.BodyParts, 0, out var current, out var maximum);

        Assert.IsFalse(found);
        Assert.AreEqual(0f, current);
        Assert.AreEqual(0f, maximum);
    }
}
