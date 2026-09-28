using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Burning.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier.Components;
using Game.World;

namespace Tests.Modules.Health;

[TestClass]
public sealed class HealthFloatingTextTests
{
    private const int EntityId = 0;

    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool(float currentHealth = 100, float maximumHealth = 100)
    {
        var pool = new PackedComponentPool<SimpleHealthComponent>(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);
        pool.Add(EntityId, new SimpleHealthComponent(currentHealth, maximumHealth));
        return pool;
    }

    private static TestFloatingText LocalEntity() => new TestFloatingText().Place(EntityId, ProcessingTierLevel.Local);

    [TestMethod]
    public void Damage_Direct_PublishesDamageTaken()
    {
        var floatingText = LocalEntity();

        TestHealth.Damage(CreateHealthPool(), new EventBus(), EntityId, 12, ActionSource.Admin, null, "Test", now: 0, floatingTextFeed: floatingText.Feed);

        Assert.HasCount(1, floatingText.Published);
        Assert.AreEqual(FloatingTextKind.DamageTaken, floatingText.Published[0].Kind);
        Assert.AreEqual(12, floatingText.Published[0].Amount);
    }

    [TestMethod]
    public void Damage_StatusEffect_PublishesStatusEffectDamageTaken()
    {
        var floatingText = LocalEntity();

        TestHealth.Damage(CreateHealthPool(), new EventBus(), EntityId, 3, ActionSource.Admin, null, "Test", now: 0, floatingTextFeed: floatingText.Feed, damageCategory: DamageCategory.StatusEffect);

        Assert.AreEqual(FloatingTextKind.StatusEffectDamageTaken, floatingText.Published.Single().Kind);
    }

    [TestMethod]
    public void Damage_Overkill_PublishesFullAmount()
    {
        var floatingText = LocalEntity();

        TestHealth.Damage(CreateHealthPool(currentHealth: 5), new EventBus(), EntityId, 40, ActionSource.Admin, null, "Test", now: 0, floatingTextFeed: floatingText.Feed);

        Assert.AreEqual(40, floatingText.Published.Single().Amount);
    }

    [TestMethod]
    public void Damage_ZeroAmount_PublishesNothing()
    {
        var floatingText = LocalEntity();

        TestHealth.Damage(CreateHealthPool(), new EventBus(), EntityId, 0, ActionSource.Admin, null, "Test", now: 0, floatingTextFeed: floatingText.Feed);

        Assert.IsEmpty(floatingText.Published);
    }

    [TestMethod]
    public void Damage_AlreadyDead_PublishesNothing()
    {
        var floatingText = LocalEntity();
        var deadEntities = EmptyPools.Packed<DeadComponent>();
        deadEntities.Add(EntityId, new DeadComponent(ActionSource.Admin, DiedAtFrame: 0));

        TestHealth.Damage(CreateHealthPool(currentHealth: 0), new EventBus(), EntityId, 5, ActionSource.Admin, null, "Test", now: 0, deadEntities: deadEntities, floatingTextFeed: floatingText.Feed);

        Assert.IsEmpty(floatingText.Published);
    }

    [TestMethod]
    public void Damage_ComplexEntity_PublishesDamageTaken()
    {
        var floatingText = LocalEntity();
        var bodyParts = BodyPartTestWorld.WithParts(EntityId, ("Torso", BodyPartType.Torso, 60, 60, true)).BodyParts;
        var health = new PackedComponentPool<SimpleHealthComponent>(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

        TestHealth.Damage(health, new EventBus(), EntityId, 9, ActionSource.Admin, null, "Test", now: 0, bodyParts: bodyParts, mathUtility: new MathUtility(new Random(1)), floatingTextFeed: floatingText.Feed);

        Assert.AreEqual(9, floatingText.Published.Single().Amount);
    }

    [TestMethod]
    public void Damage_ComplexEntityAllParts_PublishesOneTextForTheWholeHit()
    {
        var floatingText = LocalEntity();
        var bodyParts = BodyPartTestWorld.WithParts(EntityId, ("Head", BodyPartType.Head, 30, 30, true), ("Torso", BodyPartType.Torso, 60, 60, true)).BodyParts;
        var health = new PackedComponentPool<SimpleHealthComponent>(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

        TestHealth.Damage(health, new EventBus(), EntityId, 10, ActionSource.Admin, null, "Test", now: 0, bodyParts: bodyParts, mathUtility: new MathUtility(new Random(1)), targetMode: BodyPartTargetMode.All, floatingTextFeed: floatingText.Feed);

        Assert.AreEqual(10, floatingText.Published.Single().Amount);
    }

    [TestMethod]
    public void BodyPartBurningTick_PublishesStatusEffectDamageTaken()
    {
        var floatingText = LocalEntity();
        var bodyParts = BodyPartTestWorld.WithParts(EntityId, ("Torso", BodyPartType.Torso, 60, 60, true)).BodyParts;
        var timers = new MultiComponentPool<BodyPartBurningTimerComponent>(entityCapacity: 10, initialCapacity: 8);
        timers.Add(EntityId, new BodyPartBurningTimerComponent(partId: 0, stackCount: 4, nextTickFrame: 1, ActionSource.Admin));
        var health = new PackedComponentPool<SimpleHealthComponent>(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);
        var system = TestSystems.BodyPartBurningSystem(timers, bodyParts, health, new EventBus(), null, floatingTextFeed: floatingText.Feed);

        system.Update(new EngineTime(default, default, false, 1), 0);

        Assert.AreEqual(new FloatingTextEvent(EntityId, FloatingTextKind.StatusEffectDamageTaken, 4, default, new Vector2Byte(1, 1)), floatingText.Published.Single());
    }

    [TestMethod]
    public void Heal_Direct_PublishesHealthActuallyGained()
    {
        var floatingText = LocalEntity();

        TestHealth.Heal(CreateHealthPool(currentHealth: 95), EntityId, 0.5f, now: 0, floatingTextFeed: floatingText.Feed);

        Assert.AreEqual(new FloatingTextEvent(EntityId, FloatingTextKind.Healed, 5, default, new Vector2Byte(1, 1)), floatingText.Published.Single());
    }

    [TestMethod]
    public void Heal_AtFullHealth_PublishesNothing()
    {
        var floatingText = LocalEntity();

        TestHealth.Heal(CreateHealthPool(), EntityId, 0.5f, now: 0, floatingTextFeed: floatingText.Feed);

        Assert.IsEmpty(floatingText.Published);
    }

    [TestMethod]
    public void Heal_FractionThatDoesNotMoveTheDisplayedValue_PublishesNothing()
    {
        var floatingText = LocalEntity();

        TestHealth.Heal(CreateHealthPool(currentHealth: 10.2f), EntityId, 0f, now: 0, flatAmount: 0.5f, floatingTextFeed: floatingText.Feed);

        Assert.IsEmpty(floatingText.Published);
    }

    [TestMethod]
    public void Heal_FractionThatMovesTheDisplayedValue_PublishesTheDisplayedChange()
    {
        var floatingText = LocalEntity();

        TestHealth.Heal(CreateHealthPool(currentHealth: 10.8f), EntityId, 0f, now: 0, flatAmount: 0.5f, floatingTextFeed: floatingText.Feed);

        Assert.AreEqual(1, floatingText.Published.Single().Amount);
    }

    [TestMethod]
    public void Heal_Regeneration_PublishesRegenerated()
    {
        var floatingText = LocalEntity();

        TestHealth.Heal(CreateHealthPool(currentHealth: 50), EntityId, 0.2f, now: 0, floatingTextFeed: floatingText.Feed, healCategory: HealCategory.Regeneration);

        Assert.AreEqual(new FloatingTextEvent(EntityId, FloatingTextKind.Regenerated, 20, default, new Vector2Byte(1, 1)), floatingText.Published.Single());
    }

    [TestMethod]
    public void Heal_RegenerationTicksBelowAWholePoint_PublishOnlyWhenTheDisplayedValueMoves()
    {
        var floatingText = LocalEntity();
        var health = CreateHealthPool(currentHealth: 50.1f);

        for (var tick = 0; tick < 10; tick++)
        {
            TestHealth.Heal(health, EntityId, 0f, now: 0, flatAmount: 0.3f, floatingTextFeed: floatingText.Feed, healCategory: HealCategory.Regeneration);
        }

        Assert.AreEqual(53.1f, health.GetReadonly(EntityId).CurrentHealth, 0.001f);
        Assert.HasCount(3, floatingText.Published);
        Assert.IsTrue(floatingText.Published.All(static text => text is { Kind: FloatingTextKind.Regenerated, Amount: 1 }));
    }

    [TestMethod]
    public void Damage_Critical_PublishesTheCriticalFlag()
    {
        var floatingText = LocalEntity();

        HealthDamage.Apply(CreateHealthPool(), new EventBus(), EntityId, 30, ActionSource.Admin, TestPlayerQuery.NoPlayer, "Test", now: 0,
            EmptyPools.Multi<Game.Modules.StatModifiers.Components.StatModifierComponent>(), EmptyPools.BodyParts(), null, EmptyPools.Packed<DeadComponent>(),
            floatingText.Feed, DamageCategory.Direct, isCritical: true);

        Assert.AreEqual(FloatingTextFlags.Critical, floatingText.Published.Single().Flags);
    }

    [TestMethod]
    public void Heal_ComplexEntity_PublishesHealthActuallyGained()
    {
        var floatingText = LocalEntity();
        var bodyParts = BodyPartTestWorld.WithParts(EntityId, ("Torso", BodyPartType.Torso, 50, 100, true)).BodyParts;
        var health = new PackedComponentPool<SimpleHealthComponent>(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

        TestHealth.Heal(health, EntityId, 0.25f, now: 0, bodyParts: bodyParts, floatingTextFeed: floatingText.Feed);

        Assert.AreEqual(25, floatingText.Published.Single().Amount);
    }

    [TestMethod]
    public void Heal_EntityOutsideLocalTier_PublishesNothing()
    {
        var floatingText = new TestFloatingText().Place(EntityId, ProcessingTierLevel.Neighborhood);

        TestHealth.Heal(CreateHealthPool(currentHealth: 50), EntityId, 0.2f, now: 0, floatingTextFeed: floatingText.Feed);

        Assert.IsEmpty(floatingText.Published);
    }
}
