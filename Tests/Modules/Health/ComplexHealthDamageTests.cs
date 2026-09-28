using Engine.ECS.Systems;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Engine.Utilities;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Game.Modules.AbilityScores.Components;

namespace Tests.Modules.Health;

[TestClass]
public sealed class ComplexHealthDamageTests
{
    /// <summary>Always returns minValue from Next(int, int) -- BodyPartSelection.PickRandom then always lands on ordinal 0, the head of entityId's chain (the most recently Add()-ed part, since MultiComponentPool.Add inserts at the chain's head).</summary>
    private sealed class FirstPartRandom : Random
    {
        public override int Next(int minValue, int maxValue) => minValue;
    }

    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool() =>
        new(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static PackedComponentPool<DeadComponent> CreateDeadPool() =>
        new(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Apply_DamageLandsOnExactlyOnePart()
    {
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Head", BodyPartType.Head, 30, 30, true), ("Torso", BodyPartType.Torso, 60, 60, true)).BodyParts;
        var mathUtility = new MathUtility(new FirstPartRandom());

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, new EventBus(), 0, 10, ActionSource.Admin, playerQuery: TestPlayerQuery.NoPlayer, "Test", statModifiers: EmptyPools.Multi<StatModifierComponent>(), mathUtility, deadEntities: EmptyPools.Packed<DeadComponent>(), now: 0);

        // FirstPartRandom always selects ordinal 0 -- the first part of the body plan, i.e. Head.
        bodyParts.TryGet(0, 0, out var hitPart);
        Assert.AreEqual("Head", hitPart.Name);
        Assert.AreEqual(20, hitPart.CurrentHealth);

        bodyParts.TryGet(0, 1, out var otherPart);
        Assert.AreEqual("Torso", otherPart.Name);
        Assert.AreEqual(60, otherPart.CurrentHealth);
    }

    [TestMethod]
    public void Apply_HitDropsNonVitalPartToZero_SetsDisabledAndLockout()
    {
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Arm", BodyPartType.Arm, 5, 20, false)).BodyParts;
        var mathUtility = new MathUtility(new FirstPartRandom());

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, new EventBus(), 0, 10, ActionSource.Admin, playerQuery: TestPlayerQuery.NoPlayer, "Test", statModifiers: EmptyPools.Multi<StatModifierComponent>(), mathUtility, deadEntities: EmptyPools.Packed<DeadComponent>(), now: 0);

        bodyParts.TryGet(0, 0, out var part);
        Assert.AreEqual(0, part.CurrentHealth);
        Assert.IsTrue(part.IsDisabled);
        Assert.AreEqual((uint)(10 * GameTiming.FramesPerSecond), part.RegenLockedUntilFrame);
    }

    [TestMethod]
    public void Apply_HitDropsVitalPartToZero_PublishesEntityDiedExactlyOnce()
    {
        var bodyParts = BodyPartTestWorld.WithParts(1, ("Head", BodyPartType.Head, 5, 30, true)).BodyParts;
        var mathUtility = new MathUtility(new FirstPartRandom());
        var eventBus = new EventBus();
        var deadEntities = CreateDeadPool();
        var publishCount = 0;
        eventBus.Subscribe<EntityDiedEvent>(_ => publishCount++);

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, eventBus, 1, 10, TestSources.Entity(0), new TestPlayerQuery(0), "Test", statModifiers: EmptyPools.Multi<StatModifierComponent>(), mathUtility, deadEntities, now: 0);
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.AreEqual(1, publishCount);

        // Simulates DeathSystem having already marked the entity dead in response to the first EntityDiedEvent.
        deadEntities.Add(1, new DeadComponent(KilledBy: ActionSource.Admin, DiedAtFrame: 0));

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, eventBus, 1, 10, TestSources.Entity(0), new TestPlayerQuery(0), "Test", statModifiers: EmptyPools.Multi<StatModifierComponent>(), mathUtility, deadEntities, now: 0);
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.AreEqual(1, publishCount, "A subsequent hit against an already-dead entity must not republish EntityDiedEvent.");
    }

    [TestMethod]
    public void Apply_NoBodyPartComponent_ReturnsWithoutThrowing()
    {
        var bodyParts = new BodyPartTestWorld().BodyParts;
        var mathUtility = new MathUtility(new FirstPartRandom());

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, new EventBus(), 0, 10, ActionSource.Admin, playerQuery: TestPlayerQuery.NoPlayer, "Test", statModifiers: EmptyPools.Multi<StatModifierComponent>(), mathUtility, deadEntities: EmptyPools.Packed<DeadComponent>(), now: 0);
    }

    [TestMethod]
    public void Apply_IncomingDamageModifierReducesAmount()
    {
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Torso", BodyPartType.Torso, 60, 60, true)).BodyParts;
        var mathUtility = new MathUtility(new FirstPartRandom());
        var statModifiers = new MultiComponentPool<StatModifierComponent>(entityCapacity: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.IncomingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: false, magnitude: -5f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, new EventBus(), 0, 10, ActionSource.Admin, playerQuery: TestPlayerQuery.NoPlayer, "Test", statModifiers, mathUtility, deadEntities: EmptyPools.Packed<DeadComponent>(), now: 0);

        bodyParts.TryGet(0, 0, out var part);
        Assert.AreEqual(55, part.CurrentHealth);
    }

    [TestMethod]
    public void Apply_ClampsAgainstEffectiveMaximumHealth()
    {
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Torso", BodyPartType.Torso, 30, 60, true)).BodyParts;
        var mathUtility = new MathUtility(new FirstPartRandom());
        var statModifiers = new MultiComponentPool<StatModifierComponent>(entityCapacity: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Additive, StatModifierPolarity.Debuff,
            canModify: false, magnitude: -55f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, new EventBus(), 0, 0, ActionSource.Admin, playerQuery: TestPlayerQuery.NoPlayer, "Test", statModifiers, mathUtility, deadEntities: EmptyPools.Packed<DeadComponent>(), now: 0);

        bodyParts.TryGet(0, 0, out var part);
        Assert.AreEqual(5, part.CurrentHealth);
    }

    [TestMethod]
    public void Apply_PlayerInvolved_PublishesEntityDamagedWithSummedTotalNotSinglePart()
    {
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Head", BodyPartType.Head, 30, 30, true), ("Torso", BodyPartType.Torso, 60, 60, true)).BodyParts;
        var mathUtility = new MathUtility(new FirstPartRandom());
        var eventBus = new EventBus();
        EntityDamagedEvent? published = null;
        eventBus.Subscribe<EntityDamagedEvent>(e => published = e);

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, eventBus, 0, 10, ActionSource.Admin, new TestPlayerQuery(0), "Test", statModifiers: EmptyPools.Multi<StatModifierComponent>(), mathUtility, deadEntities: EmptyPools.Packed<DeadComponent>(), now: 0);

        Assert.IsNotNull(published);
        // Torso (the selected part) drops from 60 to 50; Head stays at 30 -- summed total 80, not Torso's own 50.
        Assert.AreEqual(80, published!.Value.CurrentHealth);
        Assert.AreEqual(90, published.Value.MaximumHealth);
    }

    [TestMethod]
    public void Apply_NoPlayerInvolvement_DoesNotPublishEntityDamaged()
    {
        var bodyParts = BodyPartTestWorld.WithParts(1, ("Torso", BodyPartType.Torso, 60, 60, true)).BodyParts;
        var mathUtility = new MathUtility(new FirstPartRandom());
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDamagedEvent>(_ => published = true);

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, eventBus, 1, 10, TestSources.Entity(2), new TestPlayerQuery(0), "Test", statModifiers: EmptyPools.Multi<StatModifierComponent>(), mathUtility, deadEntities: EmptyPools.Packed<DeadComponent>(), now: 0);

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Apply_TargetRuleWithMatchingTypePresent_LandsOnThatType()
    {
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Head", BodyPartType.Head, 30, 30, true), ("Torso", BodyPartType.Torso, 60, 60, true)).BodyParts;
        var mathUtility = new MathUtility(new FirstPartRandom());
        var targetRule = new BodyPartTargetRule(BodyPartType.Head, BodyPartFallback.Random);

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, new EventBus(), 0, 10, ActionSource.Admin, playerQuery: TestPlayerQuery.NoPlayer, "Test", statModifiers: EmptyPools.Multi<StatModifierComponent>(), mathUtility, deadEntities: EmptyPools.Packed<DeadComponent>(), now: 0, targetRule: targetRule);

        bodyParts.TryGet(0, BodyPartSelection.PickByType(bodyParts, 0, BodyPartType.Head), out var headPart);
        Assert.AreEqual(20, headPart.CurrentHealth);
        bodyParts.TryGet(0, BodyPartSelection.PickByType(bodyParts, 0, BodyPartType.Torso), out var torsoPart);
        Assert.AreEqual(60, torsoPart.CurrentHealth, "Torso must be untouched -- the hit landed on Head.");
    }

    [TestMethod]
    public void Apply_TargetRuleWithNoMatchingType_FallsBackPerRule()
    {
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Head", BodyPartType.Head, 30, 30, true), ("Torso", BodyPartType.Torso, 60, 60, true)).BodyParts;
        var mathUtility = new MathUtility(new FirstPartRandom());
        // No Foot part exists -- Bottommost fallback must select Torso, the lower-VerticalPosition of the two.
        var targetRule = new BodyPartTargetRule(BodyPartType.Foot, BodyPartFallback.Bottommost);

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, new EventBus(), 0, 10, ActionSource.Admin, playerQuery: TestPlayerQuery.NoPlayer, "Test", statModifiers: EmptyPools.Multi<StatModifierComponent>(), mathUtility, deadEntities: EmptyPools.Packed<DeadComponent>(), now: 0, targetRule: targetRule);

        bodyParts.TryGet(0, BodyPartSelection.PickByType(bodyParts, 0, BodyPartType.Torso), out var torsoPart);
        Assert.AreEqual(50, torsoPart.CurrentHealth);
        bodyParts.TryGet(0, BodyPartSelection.PickByType(bodyParts, 0, BodyPartType.Head), out var headPart);
        Assert.AreEqual(30, headPart.CurrentHealth, "Head must be untouched -- the fallback landed on the bottommost part, Torso.");
    }
}
