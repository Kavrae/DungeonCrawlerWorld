using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

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

        var found = HealthQueries.TryGetTotals(simpleHealth, world.BodyParts, EmptyPools.Multi<StatModifierComponent>(), 0, out var current, out var maximum);

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

        var found = HealthQueries.TryGetTotals(simpleHealth, world.BodyParts, EmptyPools.Multi<StatModifierComponent>(), 0, out var current, out var maximum);

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
        simpleHealth.Add(0, new SimpleHealthComponent(currentHealth: 50, baseMaximumHealth: 100));

        var found = HealthQueries.TryGetTotals(simpleHealth, world.BodyParts, EmptyPools.Multi<StatModifierComponent>(), 0, out var current, out var maximum);

        Assert.IsTrue(found);
        Assert.AreEqual(50f, current);
        Assert.AreEqual(100f, maximum);
    }

    [TestMethod]
    public void TryGetTotals_MaximumHealthModifier_GivesTheModifiedMaximum()
    {
        var simpleHealth = CreateSimplePool();
        var world = CreateBodyParts();
        world.Give(0);
        var statModifiers = EmptyPools.Multi<StatModifierComponent>();
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: true, 1f, FrameDeadline.Never, ActionSource.Admin));

        var found = HealthQueries.TryGetTotals(simpleHealth, world.BodyParts, statModifiers, 0, out _, out var maximum);

        Assert.IsTrue(found);
        Assert.AreEqual(90f, maximum, "the base 45 doubled, in the same units as current health");
    }

    [TestMethod]
    public void TryGetTotals_AdditiveMaximumHealthModifier_AddsToEveryPart()
    {
        var simpleHealth = CreateSimplePool();
        var world = CreateBodyParts();
        world.Give(0);
        var statModifiers = EmptyPools.Multi<StatModifierComponent>();
        var partCount = world.BodyParts.Count(0);
        HealthQueries.TryGetTotals(simpleHealth, world.BodyParts, statModifiers, 0, out _, out var unmodifiedMaximum);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Additive, StatModifierPolarity.Buff, canModify: true, 10f, FrameDeadline.Never, ActionSource.Admin));

        HealthQueries.TryGetTotals(simpleHealth, world.BodyParts, statModifiers, 0, out _, out var maximum);

        Assert.IsGreaterThan(1, partCount);
        Assert.AreEqual(unmodifiedMaximum + (10f * partCount), maximum, 0.001f, "Each part's own maximum rises by 10, as every per-part reader gives it.");
    }

    [TestMethod]
    public void TryGetBaseMaximum_SumsEveryPartsBase_AndSimpleHealthReadsItsOwn()
    {
        var simpleHealth = CreateSimplePool();
        var world = CreateBodyParts();
        world.Give(0);
        simpleHealth.Add(1, new SimpleHealthComponent(currentHealth: 150, baseMaximumHealth: 100));

        Assert.IsTrue(HealthQueries.TryGetBaseMaximum(simpleHealth, world.BodyParts, 0, out var partsBase));
        Assert.IsTrue(HealthQueries.TryGetBaseMaximum(simpleHealth, world.BodyParts, 1, out var simpleBase));
        Assert.AreEqual(45f, partsBase);
        Assert.AreEqual(100f, simpleBase);
        Assert.IsFalse(HealthQueries.TryGetBaseMaximum(simpleHealth, world.BodyParts, 2, out _));
    }

    [TestMethod]
    public void TryGetTotals_EntityHasNeither_ReturnsFalse()
    {
        var simpleHealth = CreateSimplePool();
        var world = CreateBodyParts();

        var found = HealthQueries.TryGetTotals(simpleHealth, world.BodyParts, EmptyPools.Multi<StatModifierComponent>(), 0, out var current, out var maximum);

        Assert.IsFalse(found);
        Assert.AreEqual(0f, current);
        Assert.AreEqual(0f, maximum);
    }

    private static MultiComponentPool<StatModifierComponent> ModifiersOf(BodyPartTestWorld world) =>
        world.Components.GetMultiPool<StatModifierComponent>();

    private static StatModifierComponent MaximumHealthMultiplier(float magnitude) =>
        new(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: true, magnitude, FrameDeadline.Never, ActionSource.Admin);

    [TestMethod]
    public void HasMissingHealth_SimpleHealth_IsTrueOnlyBelowTheMaximum()
    {
        var simpleHealth = CreateSimplePool();
        var world = CreateBodyParts();
        simpleHealth.Add(0, new SimpleHealthComponent(currentHealth: 100, baseMaximumHealth: 100));
        simpleHealth.Add(1, new SimpleHealthComponent(currentHealth: 99, baseMaximumHealth: 100));

        Assert.IsFalse(HealthQueries.HasMissingHealth(simpleHealth, world.BodyParts, ModifiersOf(world), 0));
        Assert.IsTrue(HealthQueries.HasMissingHealth(simpleHealth, world.BodyParts, ModifiersOf(world), 1));
    }

    [TestMethod]
    public void HasMissingHealth_SimpleHealthAtItsStoredMaximumWithARaisedMaximum_IsTrue()
    {
        var simpleHealth = CreateSimplePool();
        var world = CreateBodyParts();
        simpleHealth.Add(0, new SimpleHealthComponent(currentHealth: 100, baseMaximumHealth: 100));
        ModifiersOf(world).Add(0, MaximumHealthMultiplier(0.5f));

        Assert.IsTrue(HealthQueries.HasMissingHealth(simpleHealth, world.BodyParts, ModifiersOf(world), 0));
    }

    [TestMethod]
    public void HasMissingHealth_BodyParts_IsTrueWhenAnyOnePartIsBelowItsMaximum()
    {
        var simpleHealth = CreateSimplePool();
        var world = CreateBodyParts();
        world.Give(0);

        Assert.IsFalse(HealthQueries.HasMissingHealth(simpleHealth, world.BodyParts, ModifiersOf(world), 0));

        world.SetHealth(0, 2, 14);

        Assert.IsTrue(HealthQueries.HasMissingHealth(simpleHealth, world.BodyParts, ModifiersOf(world), 0));
    }

    [TestMethod]
    public void HasMissingHealth_BodyPartsAtTheirStoredMaximumWithARaisedMaximum_IsTrue()
    {
        var simpleHealth = CreateSimplePool();
        var world = CreateBodyParts();
        world.Give(0);
        world.SetHealth(0, 0, 10);
        ModifiersOf(world).Add(0, MaximumHealthMultiplier(0.5f));

        Assert.IsTrue(HealthQueries.HasMissingHealth(simpleHealth, world.BodyParts, ModifiersOf(world), 0));
    }

    [TestMethod]
    public void HasMissingHealth_EntityHasNeither_IsFalse()
    {
        var simpleHealth = CreateSimplePool();
        var world = CreateBodyParts();

        Assert.IsFalse(HealthQueries.HasMissingHealth(simpleHealth, world.BodyParts, ModifiersOf(world), 0));
    }
}
