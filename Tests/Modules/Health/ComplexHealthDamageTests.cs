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

namespace Tests.Modules.Health;

[TestClass]
public sealed class ComplexHealthDamageTests
{
    private sealed class FakePlayerQuery(int playerEntityId) : IPlayerQuery
    {
        public int PlayerEntityId { get; } = playerEntityId;
    }

    /// <summary>Always returns minValue from Next(int, int) -- BodyPartSelection.PickRandom then always lands on ordinal 0, the head of entityId's chain (the most recently Add()-ed part, since MultiComponentPool.Add inserts at the chain's head).</summary>
    private sealed class FirstPartRandom : Random
    {
        public override int Next(int minValue, int maxValue) => minValue;
    }

    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static MultiComponentPool<BodyPartComponent> CreateBodyPartsPool() =>
        new(maximumEntityCount: 10, initialCapacity: 8);

    private static PackedComponentPool<DeadComponent> CreateDeadPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Apply_DamageLandsOnExactlyOnePart()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 0, currentHealth: 30, maximumHealth: 30, isVital: true));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 60, maximumHealth: 60, isVital: true));
        var mathUtility = new MathUtility(new FirstPartRandom());

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, new EventBus(), 0, 10, StatusEffectSource.Admin, playerQuery: null, "Test", statModifiers: null, mathUtility, deadEntities: null);

        // FirstPartRandom always selects ordinal 0 -- the head of the chain, i.e. Torso (added last).
        var headDenseIndex = bodyParts.GetFirstDenseIndex(0);
        var torsoPart = bodyParts.GetReadonlyByDenseIndex(headDenseIndex);
        Assert.AreEqual("Torso", torsoPart.Name);
        Assert.AreEqual(50, torsoPart.CurrentHealth);

        var otherDenseIndex = bodyParts.GetNextDenseIndex(headDenseIndex);
        var otherPart = bodyParts.GetReadonlyByDenseIndex(otherDenseIndex);
        Assert.AreEqual("Head", otherPart.Name);
        Assert.AreEqual(30, otherPart.CurrentHealth);
    }

    [TestMethod]
    public void Apply_HitDropsNonVitalPartToZero_SetsDisabledAndLockout()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Arm", BodyPartType.Arm, 0, 0, currentHealth: 5, maximumHealth: 20, isVital: false));
        var mathUtility = new MathUtility(new FirstPartRandom());

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, new EventBus(), 0, 10, StatusEffectSource.Admin, playerQuery: null, "Test", statModifiers: null, mathUtility, deadEntities: null);

        var part = bodyParts.GetReadonlyByDenseIndex(bodyParts.GetFirstDenseIndex(0));
        Assert.AreEqual(0, part.CurrentHealth);
        Assert.IsTrue(part.IsDisabled);
        Assert.AreEqual(10 * GameTiming.FramesPerSecond, part.RegenLockoutFramesRemaining);
    }

    [TestMethod]
    public void Apply_HitDropsVitalPartToZero_PublishesEntityDiedExactlyOnce()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(1, new BodyPartComponent("Head", BodyPartType.Head, 0, 0, currentHealth: 5, maximumHealth: 30, isVital: true));
        var mathUtility = new MathUtility(new FirstPartRandom());
        var eventBus = new EventBus();
        var deadEntities = CreateDeadPool();
        var publishCount = 0;
        eventBus.Subscribe<EntityDiedEvent>(_ => publishCount++);

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, eventBus, 1, 10, StatusEffectSource.FromEntity(0), new FakePlayerQuery(0), "Test", statModifiers: null, mathUtility, deadEntities);
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.AreEqual(1, publishCount);

        // Simulates DeathSystem having already marked the entity dead in response to the first EntityDiedEvent.
        deadEntities.Add(1, new DeadComponent(KilledByEntityId: 0, DiedAtFrame: 0));

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, eventBus, 1, 10, StatusEffectSource.FromEntity(0), new FakePlayerQuery(0), "Test", statModifiers: null, mathUtility, deadEntities);
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.AreEqual(1, publishCount, "A subsequent hit against an already-dead entity must not republish EntityDiedEvent.");
    }

    [TestMethod]
    public void Apply_NoBodyPartComponent_ReturnsWithoutThrowing()
    {
        var bodyParts = CreateBodyPartsPool();
        var mathUtility = new MathUtility(new FirstPartRandom());

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, new EventBus(), 0, 10, StatusEffectSource.Admin, playerQuery: null, "Test", statModifiers: null, mathUtility, deadEntities: null);
    }

    [TestMethod]
    public void Apply_IncomingDamageModifierReducesAmount()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 60, maximumHealth: 60, isVital: true));
        var mathUtility = new MathUtility(new FirstPartRandom());
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.IncomingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: false, magnitude: -5f, remainingDurationFrames: null, StatusEffectSource.Admin));

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, new EventBus(), 0, 10, StatusEffectSource.Admin, playerQuery: null, "Test", statModifiers, mathUtility, deadEntities: null);

        var part = bodyParts.GetReadonlyByDenseIndex(bodyParts.GetFirstDenseIndex(0));
        Assert.AreEqual(55, part.CurrentHealth);
    }

    [TestMethod]
    public void Apply_ClampsAgainstEffectiveMaximumHealth()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 30, maximumHealth: 60, isVital: true));
        var mathUtility = new MathUtility(new FirstPartRandom());
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Additive, StatModifierPolarity.Debuff,
            canModify: false, magnitude: -55f, remainingDurationFrames: null, StatusEffectSource.Admin));

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, new EventBus(), 0, 0, StatusEffectSource.Admin, playerQuery: null, "Test", statModifiers, mathUtility, deadEntities: null);

        var part = bodyParts.GetReadonlyByDenseIndex(bodyParts.GetFirstDenseIndex(0));
        Assert.AreEqual(5, part.CurrentHealth);
    }

    [TestMethod]
    public void Apply_PlayerInvolved_PublishesEntityDamagedWithSummedTotalNotSinglePart()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 0, currentHealth: 30, maximumHealth: 30, isVital: true));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 60, maximumHealth: 60, isVital: true));
        var mathUtility = new MathUtility(new FirstPartRandom());
        var eventBus = new EventBus();
        EntityDamagedEvent? published = null;
        eventBus.Subscribe<EntityDamagedEvent>(e => published = e);

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, eventBus, 0, 10, StatusEffectSource.Admin, new FakePlayerQuery(0), "Test", statModifiers: null, mathUtility, deadEntities: null);

        Assert.IsNotNull(published);
        // Torso (the selected part) drops from 60 to 50; Head stays at 30 -- summed total 80, not Torso's own 50.
        Assert.AreEqual(80, published!.Value.CurrentHealth);
        Assert.AreEqual(90, published.Value.MaximumHealth);
    }

    [TestMethod]
    public void Apply_NoPlayerInvolvement_DoesNotPublishEntityDamaged()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(1, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 60, maximumHealth: 60, isVital: true));
        var mathUtility = new MathUtility(new FirstPartRandom());
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDamagedEvent>(_ => published = true);

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, eventBus, 1, 10, StatusEffectSource.FromEntity(2), new FakePlayerQuery(0), "Test", statModifiers: null, mathUtility, deadEntities: null);

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Apply_TargetRuleWithMatchingTypePresent_LandsOnThatType()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 5, currentHealth: 30, maximumHealth: 30, isVital: true));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 4, currentHealth: 60, maximumHealth: 60, isVital: true));
        var mathUtility = new MathUtility(new FirstPartRandom());
        var targetRule = new BodyPartTargetRule(BodyPartType.Head, BodyPartFallback.Random);

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, new EventBus(), 0, 10, StatusEffectSource.Admin, playerQuery: null, "Test", statModifiers: null, mathUtility, deadEntities: null, targetRule);

        var headDenseIndex = BodyPartSelection.PickByType(bodyParts, 0, BodyPartType.Head);
        Assert.AreEqual(20, bodyParts.GetReadonlyByDenseIndex(headDenseIndex).CurrentHealth);
        var torsoDenseIndex = BodyPartSelection.PickByType(bodyParts, 0, BodyPartType.Torso);
        Assert.AreEqual(60, bodyParts.GetReadonlyByDenseIndex(torsoDenseIndex).CurrentHealth, "Torso must be untouched -- the hit landed on Head.");
    }

    [TestMethod]
    public void Apply_TargetRuleWithNoMatchingType_FallsBackPerRule()
    {
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 5, currentHealth: 30, maximumHealth: 30, isVital: true));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 4, currentHealth: 60, maximumHealth: 60, isVital: true));
        var mathUtility = new MathUtility(new FirstPartRandom());
        // No Foot part exists -- Bottommost fallback must select Torso, the lower-VerticalPosition of the two.
        var targetRule = new BodyPartTargetRule(BodyPartType.Foot, BodyPartFallback.Bottommost);

        ComplexHealthDamage.Apply(CreateHealthPool(), bodyParts, new EventBus(), 0, 10, StatusEffectSource.Admin, playerQuery: null, "Test", statModifiers: null, mathUtility, deadEntities: null, targetRule);

        var torsoDenseIndex = BodyPartSelection.PickByType(bodyParts, 0, BodyPartType.Torso);
        Assert.AreEqual(50, bodyParts.GetReadonlyByDenseIndex(torsoDenseIndex).CurrentHealth);
        var headDenseIndex = BodyPartSelection.PickByType(bodyParts, 0, BodyPartType.Head);
        Assert.AreEqual(30, bodyParts.GetReadonlyByDenseIndex(headDenseIndex).CurrentHealth, "Head must be untouched -- the fallback landed on the bottommost part, Torso.");
    }
}
