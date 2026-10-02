using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Effects;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Effects;

/// <summary>What entries do when the effect comes from something other than an entity's action: no source entity, a magnitude, a default body part, repeated application.</summary>
[TestClass]
public sealed class EffectOriginTests
{
    private const int SourceEntityId = 1;
    private const int TargetEntityId = 2;
    private const ushort LavaTerrainTypeId = 7;

    /// <summary>Every roll succeeds: a crit whenever one is rolled, and a chained effect always triggers.</summary>
    private sealed class AlwaysLowRandom : Random
    {
        public override double NextDouble() => 0.0;
    }

    private sealed class Fixture
    {
        public ComponentManager ComponentManager { get; } = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));

        public EventBus EventBus { get; } = new();

        public TestFloatingText FloatingText { get; } = new TestFloatingText().Place(TargetEntityId, ProcessingTierLevel.Local);

        public StatusEffectApplierRegistry Appliers { get; } = new();

        public EntityBodyParts? BodyParts { get; set; }

        public Random Random { get; set; } = new AlwaysLowRandom();

        public Fixture()
        {
            var componentManager = ComponentManager;
            var entityKeys = new EntityKeys();
            Appliers.Register(new BurningApplier(componentManager, BodyPartTestWorld.CreaturesOf(componentManager), EventBus, new TestPlayerQuery(TargetEntityId)));
            Appliers.Register(new TimerBasedStatusEffectApplier<PoisonTimerComponent>(StatusEffectType.Poison, componentManager.GetPackedPool<PoisonTimerComponent>(), PoisonEffects.MaxStacks,
                (entityId, count, source, now, announcesRefusal) => PoisonEffects.ApplyStacks(componentManager, entityKeys, entityId, count, source, durationInTicks: 5, now, EventBus, new TestPlayerQuery(TargetEntityId), announcesRefusal)));
        }

        public PackedComponentPool<SimpleHealthComponent> Health => ComponentManager.GetPackedPool<SimpleHealthComponent>();

        public MultiComponentPool<StatModifierComponent> StatModifiers => ComponentManager.GetMultiPool<StatModifierComponent>();

        public int BurningStacks => ComponentManager.GetPackedPool<BurningTimerComponent>().TryGetReadonly(TargetEntityId, out var timer) ? timer.StackCount : 0;

        /// <summary>A context with no source entity, attributed to a terrain -- what a terrain contact or an aura builds.</summary>
        public EffectContext Sourceless(float magnitude = 1f) =>
            new(Services(), ActionSource.FromTerrain(LavaTerrainTypeId), SourceEntityId: null, TargetEntityId, "Lava", ActivatorTags: [], Now: 0) { Magnitude = magnitude };

        public EffectContext FromEntity() =>
            EffectContext.FromEntity(Services(), SourceEntityId, TargetEntityId, "Test", activatorTags: [], now: 0);

        private EffectServices Services() =>
            TestActionEffects.Services(ComponentManager, new EntityKeys(), EventBus, new MathUtility(Random), Health,
                statusEffectAppliers: Appliers, statModifiers: StatModifiers, bodyParts: BodyParts, floatingTextFeed: FloatingText.Feed);
    }

    private static Fixture WithSimpleHealth(float currentHealth = 100)
    {
        var fixture = new Fixture();
        fixture.Health.Add(TargetEntityId, new SimpleHealthComponent(currentHealth, maximumHealth: 100));
        return fixture;
    }

    [TestMethod]
    public void DirectDamage_NoSourceEntity_DealsItsFlatAmountAndNeverCrits()
    {
        var fixture = WithSimpleHealth();
        fixture.StatModifiers.Add(SourceEntityId, new StatModifierComponent(StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: true, 1f, FrameDeadline.Never, ActionSource.Admin));

        var outcome = new DirectDamage(MinFlatDamage: 10, MaxFlatDamage: 10).Apply(fixture.Sourceless());

        Assert.AreEqual(EffectOutcome.Applied, outcome);
        Assert.AreEqual(90, fixture.Health.GetReadonly(TargetEntityId).CurrentHealth);
    }

    /// <summary>The same roll from an entity crits (AlwaysLowRandom), which is what the sourceless case above must not do.</summary>
    [TestMethod]
    public void DirectDamage_FromAnEntity_StillCrits()
    {
        var fixture = WithSimpleHealth();

        new DirectDamage(MinFlatDamage: 10, MaxFlatDamage: 10).Apply(fixture.FromEntity());

        Assert.AreEqual(100 - 10 * CritMath.BaseCritMultiplier, fixture.Health.GetReadonly(TargetEntityId).CurrentHealth);
    }

    [TestMethod]
    public void DirectDamage_ScalesItsFlatAmountByMagnitude()
    {
        var fixture = WithSimpleHealth();

        new DirectDamage(MinFlatDamage: 2, MaxFlatDamage: 2).Apply(fixture.Sourceless(magnitude: 8));

        Assert.AreEqual(84, fixture.Health.GetReadonly(TargetEntityId).CurrentHealth);
    }

    private static Fixture WithHeadTorsoAndFeet()
    {
        var fixture = new Fixture();
        fixture.BodyParts = BodyPartTestWorld.WithParts(fixture.ComponentManager, TargetEntityId,
            ("Head", BodyPartType.Head, 50f, (ushort)50, true),
            ("Torso", BodyPartType.Torso, 50f, (ushort)50, true),
            ("Feet", BodyPartType.Foot, 50f, (ushort)50, false)).BodyParts;
        return fixture;
    }

    private const byte HeadPartId = 0;
    private const byte TorsoPartId = 1;
    private const byte FeetPartId = 2;

    private static readonly BodyPartTargetRule BottommostPart = new(PreferredType: null, BodyPartFallback.Bottommost);

    private static float PartHealth(Fixture fixture, byte partId)
    {
        fixture.BodyParts!.TryGet(TargetEntityId, partId, out var part);
        return part.CurrentHealth;
    }

    private static int PartBurningStacks(Fixture fixture, byte partId)
    {
        var partTimers = fixture.ComponentManager.GetMultiPool<BodyPartBurningTimerComponent>();
        for (var denseIndex = partTimers.GetFirstDenseIndex(TargetEntityId); denseIndex != -1; denseIndex = partTimers.GetNextDenseIndex(denseIndex))
        {
            var timer = partTimers.GetReadonlyByDenseIndex(denseIndex);
            if (timer.PartId == partId)
            {
                return timer.StackCount;
            }
        }

        return 0;
    }

    [TestMethod]
    public void DirectDamage_AimedAtTheGroundContact_LandsOnThePartTheContextNames()
    {
        var fixture = WithHeadTorsoAndFeet();
        var context = fixture.Sourceless() with { GroundContactBodyPartRule = BottommostPart };

        new DirectDamage(MinFlatDamage: 10, MaxFlatDamage: 10, BodyPart: BodyPartTargeting.GroundContact).Apply(context);

        Assert.AreEqual(40, PartHealth(fixture, FeetPartId));
        Assert.AreEqual(50, PartHealth(fixture, HeadPartId));
        Assert.AreEqual(50, PartHealth(fixture, TorsoPartId));
    }

    /// <summary>The ground part reaches only the entries that ask for it: one that names a part of its own is unaffected by what applied it.</summary>
    [TestMethod]
    public void DirectDamage_NamingItsOwnPart_IgnoresTheGroundContact()
    {
        var fixture = WithHeadTorsoAndFeet();
        var context = fixture.Sourceless() with { GroundContactBodyPartRule = BottommostPart };

        new DirectDamage(MinFlatDamage: 10, MaxFlatDamage: 10, BodyPart: BodyPartTargeting.Of(BodyPartType.Head)).Apply(context);

        Assert.AreEqual(40, PartHealth(fixture, HeadPartId));
        Assert.AreEqual(50, PartHealth(fixture, FeetPartId));
    }

    /// <summary>Holy Ground's shape: an effect that asks for no part is the whole entity's, whatever part the ground touches.</summary>
    [TestMethod]
    public void StatusEffectGrant_NamingNoPart_IsHeldOnTheEntityEvenFromAGroundContact()
    {
        var fixture = WithHeadTorsoAndFeet();
        var context = fixture.Sourceless() with { GroundContactBodyPartRule = BottommostPart };

        new StatusEffectGrant(StatusEffectType.Burning, StackCount: 3).Apply(context);

        Assert.AreEqual(3, fixture.BurningStacks);
        Assert.IsFalse(fixture.ComponentManager.GetMultiPool<BodyPartBurningTimerComponent>().Has(TargetEntityId));
    }

    [TestMethod]
    public void StatusEffectGrant_AimedAtTheGroundContact_IsHeldOnThatPart()
    {
        var fixture = WithHeadTorsoAndFeet();
        var context = fixture.Sourceless() with { GroundContactBodyPartRule = BottommostPart };
        var grant = new StatusEffectGrant(StatusEffectType.Burning, StackCount: 8, StatusEffectGrantMode.TopUpTo, BodyPartTargeting.GroundContact);

        grant.Apply(context);
        grant.Apply(context);

        Assert.AreEqual(8, PartBurningStacks(fixture, FeetPartId));
        Assert.AreEqual(0, fixture.BurningStacks);
        Assert.AreEqual(1, fixture.ComponentManager.GetMultiPool<BodyPartBurningTimerComponent>().CountForEntity(TargetEntityId));
    }

    /// <summary>Aimed at the ground contact with no ground in the context -- an aura, an action -- names no part: the entity holds it.</summary>
    [TestMethod]
    public void StatusEffectGrant_AimedAtTheGroundContactWithNoGround_IsHeldOnTheEntity()
    {
        var fixture = WithHeadTorsoAndFeet();

        new StatusEffectGrant(StatusEffectType.Burning, StackCount: 2, BodyPart: BodyPartTargeting.GroundContact).Apply(fixture.Sourceless());

        Assert.AreEqual(2, fixture.BurningStacks);
    }

    [TestMethod]
    public void StatusEffectGrant_AimedAtAPartType_IsHeldOnThatPart()
    {
        var fixture = WithHeadTorsoAndFeet();

        new StatusEffectGrant(StatusEffectType.Burning, StackCount: 2, BodyPart: BodyPartTargeting.Of(BodyPartType.Head)).Apply(fixture.Sourceless());

        Assert.AreEqual(2, PartBurningStacks(fixture, HeadPartId));
        Assert.AreEqual(0, fixture.BurningStacks);
    }

    /// <summary>The lava aura's shape: a random part topped up each time, and each part's stacks are its own, so repeated applications set more and more of the entity alight.</summary>
    [TestMethod]
    public void StatusEffectGrant_AimedAtARandomPart_ToppedUpRepeatedly_SpreadsAcrossTheParts()
    {
        var fixture = WithHeadTorsoAndFeet();
        fixture.Random = new Random(7);
        var grant = new StatusEffectGrant(StatusEffectType.Burning, StackCount: 1, StatusEffectGrantMode.TopUpTo, BodyPartTargeting.Random);

        for (var application = 0; application < 30; application++)
        {
            grant.Apply(fixture.Sourceless(magnitude: 4));
        }

        Assert.AreEqual(4, PartBurningStacks(fixture, HeadPartId));
        Assert.AreEqual(4, PartBurningStacks(fixture, TorsoPartId));
        Assert.AreEqual(4, PartBurningStacks(fixture, FeetPartId));
        Assert.AreEqual(0, fixture.BurningStacks, "Nothing is held on the entity as a whole.");
    }

    [TestMethod]
    public void StatusEffectGrant_AimedAtARandomPart_OnAnEntityWithoutBodyParts_IsHeldOnTheEntity()
    {
        var fixture = WithSimpleHealth();

        new StatusEffectGrant(StatusEffectType.Burning, StackCount: 1, StatusEffectGrantMode.TopUpTo, BodyPartTargeting.Random).Apply(fixture.Sourceless(magnitude: 4));

        Assert.AreEqual(4, fixture.BurningStacks);
    }

    /// <summary>An effect that isn't held per body part ignores the part and lands on the entity.</summary>
    [TestMethod]
    public void StatusEffectGrant_OfAnEffectNotHeldPerPart_IgnoresThePartNamed()
    {
        var fixture = WithHeadTorsoAndFeet();

        new StatusEffectGrant(StatusEffectType.Poison, StackCount: 2, BodyPart: BodyPartTargeting.Of(BodyPartType.Head)).Apply(fixture.Sourceless());

        Assert.AreEqual(2, fixture.ComponentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(TargetEntityId).StackCount);
    }

    [TestMethod]
    public void DirectHeal_ScalesItsFlatAmountByMagnitude()
    {
        var fixture = WithSimpleHealth(currentHealth: 50);

        var outcome = new DirectHeal(PercentOfMaxHealth: 0f, FlatAmount: 1f).Apply(fixture.Sourceless(magnitude: 8));

        Assert.AreEqual(EffectOutcome.Applied, outcome);
        Assert.AreEqual(58, fixture.Health.GetReadonly(TargetEntityId).CurrentHealth);
    }

    [TestMethod]
    public void DirectHeal_TargetAtFullHealth_IsNoEffect()
    {
        var fixture = WithSimpleHealth();

        Assert.AreEqual(EffectOutcome.NoEffect, new DirectHeal(PercentOfMaxHealth: 0.5f).Apply(fixture.Sourceless()));
    }

    [TestMethod]
    public void DirectHeal_BodyPartTargetWithADamagedPart_IsAppliedAndHealsIt()
    {
        var fixture = WithHeadTorsoAndFeet();
        fixture.BodyParts!.SetCurrentHealth(TargetEntityId, TorsoPartId, 20);

        var outcome = new DirectHeal(PercentOfMaxHealth: 0f, FlatAmount: 5f, BodyPartTargetMode: BodyPartTargetMode.LowestPercentage).Apply(fixture.Sourceless());

        Assert.AreEqual(EffectOutcome.Applied, outcome);
        Assert.AreEqual(25, PartHealth(fixture, TorsoPartId));
    }

    [TestMethod]
    public void DirectHeal_BodyPartTargetWithEveryPartFull_IsNoEffect()
    {
        var fixture = WithHeadTorsoAndFeet();

        Assert.AreEqual(EffectOutcome.NoEffect, new DirectHeal(PercentOfMaxHealth: 0.5f).Apply(fixture.Sourceless()));
    }

    /// <summary>A cure ends the effect wherever it is held: a burn on one body part, with nothing on the entity as a whole, is still a burn.</summary>
    [TestMethod]
    public void StatusEffectRemoval_BurnHeldOnlyOnABodyPart_RemovesItAndIsApplied()
    {
        var fixture = WithHeadTorsoAndFeet();
        new StatusEffectGrant(StatusEffectType.Burning, StackCount: 3, BodyPart: BodyPartTargeting.Of(BodyPartType.Head)).Apply(fixture.Sourceless());
        Assert.AreEqual(3, PartBurningStacks(fixture, HeadPartId), "Precondition: the burn is on the head only.");
        Assert.AreEqual(0, fixture.BurningStacks);

        var outcome = new StatusEffectRemoval(StatusEffectType.Burning).Apply(fixture.Sourceless());

        Assert.AreEqual(EffectOutcome.Applied, outcome);
        Assert.AreEqual(0, PartBurningStacks(fixture, HeadPartId));
    }

    [TestMethod]
    public void StatusEffectRemoval_BurnOnTheEntityAndOnParts_RemovesAllOfIt()
    {
        var fixture = WithHeadTorsoAndFeet();
        new StatusEffectGrant(StatusEffectType.Burning, StackCount: 3, BodyPart: BodyPartTargeting.Of(BodyPartType.Head)).Apply(fixture.Sourceless());
        new StatusEffectGrant(StatusEffectType.Burning, StackCount: 2).Apply(fixture.Sourceless());

        var outcome = new StatusEffectRemoval(StatusEffectType.Burning).Apply(fixture.Sourceless());

        Assert.AreEqual(EffectOutcome.Applied, outcome);
        Assert.AreEqual(0, fixture.BurningStacks);
        Assert.IsFalse(fixture.ComponentManager.GetMultiPool<BodyPartBurningTimerComponent>().Has(TargetEntityId));
    }

    [TestMethod]
    public void StatusEffectRemoval_TargetWithoutTheEffect_IsNoEffect()
    {
        var fixture = WithHeadTorsoAndFeet();

        Assert.AreEqual(EffectOutcome.NoEffect, new StatusEffectRemoval(StatusEffectType.Burning).Apply(fixture.Sourceless()));
        Assert.AreEqual(EffectOutcome.NoEffect, new StatusEffectRemoval(StatusEffectType.Paralysis).Apply(fixture.Sourceless()), "No applier is registered for Paralysis here.");
    }

    [TestMethod]
    public void StatusEffectGrant_TopUpTo_RaisesStacksToTheScaledCountAndNoFurther()
    {
        var fixture = WithSimpleHealth();
        var grant = new StatusEffectGrant(StatusEffectType.Burning, StackCount: 1, StatusEffectGrantMode.TopUpTo);

        Assert.AreEqual(EffectOutcome.Applied, grant.Apply(fixture.Sourceless(magnitude: 4)));
        Assert.AreEqual(4, fixture.BurningStacks);

        Assert.AreEqual(EffectOutcome.NoEffect, grant.Apply(fixture.Sourceless(magnitude: 4)));
        Assert.AreEqual(4, fixture.BurningStacks);

        Assert.AreEqual(EffectOutcome.Applied, grant.Apply(fixture.Sourceless(magnitude: 6)));
        Assert.AreEqual(6, fixture.BurningStacks);
    }

    [TestMethod]
    public void StatusEffectGrant_Add_AddsTheScaledCountEveryTime()
    {
        var fixture = WithSimpleHealth();
        var grant = new StatusEffectGrant(StatusEffectType.Burning, StackCount: 2);

        grant.Apply(fixture.Sourceless(magnitude: 2));
        grant.Apply(fixture.Sourceless(magnitude: 2));

        Assert.AreEqual(8, fixture.BurningStacks);
    }

    [TestMethod]
    public void StatusEffectGrant_MoreThanTheCap_LandsOnlyUpToIt()
    {
        var fixture = WithSimpleHealth();

        var outcome = new StatusEffectGrant(StatusEffectType.Burning, StackCount: 1, StatusEffectGrantMode.TopUpTo).Apply(fixture.Sourceless(magnitude: 52));

        Assert.AreEqual(EffectOutcome.Applied, outcome);
        Assert.AreEqual(BurningEffects.MaxStacks, fixture.BurningStacks);
        Assert.AreEqual(BurningEffects.MaxStacks, fixture.FloatingText.Published.Single().Amount);
    }

    [TestMethod]
    public void StatusEffectGrant_AtTheCap_IsNoEffectAndPublishesNoAppliedEvent()
    {
        var fixture = WithSimpleHealth();
        var grant = new StatusEffectGrant(StatusEffectType.Burning, StackCount: 1, StatusEffectGrantMode.TopUpTo);
        grant.Apply(fixture.Sourceless(magnitude: 52));
        var appliedEvents = 0;
        fixture.EventBus.Subscribe<StatusEffectAppliedEvent>(_ => appliedEvents++);

        Assert.AreEqual(EffectOutcome.NoEffect, grant.Apply(fixture.Sourceless(magnitude: 52)));
        Assert.AreEqual(0, appliedEvents);
    }

    [TestMethod]
    public void StatusEffectGrant_ImmuneTarget_IsRefusedWithOneBlockedEventHoweverManyStacks()
    {
        var fixture = WithSimpleHealth();
        StatusEffectImmunityEffects.GrantPermanent(fixture.ComponentManager.GetMultiPool<StatusEffectImmunityComponent>(), TargetEntityId, StatusEffectType.Burning);
        var blockedEvents = 0;
        var appliedEvents = 0;
        fixture.EventBus.Subscribe<StatusEffectImmunityBlockedEvent>(_ => blockedEvents++);
        fixture.EventBus.Subscribe<StatusEffectAppliedEvent>(_ => appliedEvents++);

        var outcome = new StatusEffectGrant(StatusEffectType.Burning, StackCount: 1, StatusEffectGrantMode.TopUpTo).Apply(fixture.Sourceless(magnitude: 8));

        Assert.AreEqual(EffectOutcome.Refused, outcome);
        Assert.AreEqual(0, fixture.BurningStacks);
        Assert.AreEqual(1, blockedEvents);
        Assert.AreEqual(0, appliedEvents);
        Assert.AreEqual(FloatingTextKind.Immune, fixture.FloatingText.Published.Single().Kind);
    }

    [TestMethod]
    public void StatusEffectGrant_ImmuneTargetAlreadyReported_IsRefusedSilently()
    {
        var fixture = WithSimpleHealth();
        StatusEffectImmunityEffects.GrantPermanent(fixture.ComponentManager.GetMultiPool<StatusEffectImmunityComponent>(), TargetEntityId, StatusEffectType.Burning);
        var blockedEvents = 0;
        fixture.EventBus.Subscribe<StatusEffectImmunityBlockedEvent>(_ => blockedEvents++);

        var outcome = new StatusEffectGrant(StatusEffectType.Burning, StackCount: 8).Apply(fixture.Sourceless() with { AnnouncesRefusal = false });

        Assert.AreEqual(EffectOutcome.Refused, outcome);
        Assert.AreEqual(0, blockedEvents);
        Assert.IsEmpty(fixture.FloatingText.Published);
    }

    [TestMethod]
    public void StatusEffectGrant_StacksAreAttributedToTheContextsSource()
    {
        var fixture = WithSimpleHealth();

        new StatusEffectGrant(StatusEffectType.Burning).Apply(fixture.Sourceless());

        Assert.AreEqual(ActionSource.FromTerrain(LavaTerrainTypeId), fixture.ComponentManager.GetPackedPool<BurningTimerComponent>().GetReadonly(TargetEntityId).Source);
    }

    [TestMethod]
    public void StatModifierGrant_RefreshFromSameSource_HoldsOneModifierAndMovesItsExpiry()
    {
        var fixture = WithSimpleHealth();
        var grant = new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, CanModify: true, Magnitude: -0.1f, DurationFrames: 100,
            Stacking: StatModifierStacking.RefreshFromSameSource);

        grant.Apply(fixture.Sourceless());
        grant.Apply(fixture.Sourceless() with { Now = 40 });

        Assert.AreEqual(1, fixture.StatModifiers.CountForEntity(TargetEntityId));
        Assert.AreEqual(140u, fixture.StatModifiers.GetReadonlyByDenseIndex(fixture.StatModifiers.GetFirstDenseIndex(TargetEntityId)).ExpiresAtFrame);
    }

    [TestMethod]
    public void StatModifierGrant_Add_HoldsOnePerGrant()
    {
        var fixture = WithSimpleHealth();
        var grant = new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, CanModify: true, Magnitude: -0.1f, DurationFrames: 100);

        grant.Apply(fixture.Sourceless());
        grant.Apply(fixture.Sourceless());

        Assert.AreEqual(2, fixture.StatModifiers.CountForEntity(TargetEntityId));
    }

    [TestMethod]
    public void StatModifierGrant_NoSourceEntity_KeepsItsOwnDuration()
    {
        var fixture = WithSimpleHealth();

        new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, CanModify: true, Magnitude: -0.1f, DurationFrames: 100).Apply(fixture.Sourceless());

        Assert.AreEqual(100u, fixture.StatModifiers.GetReadonlyByDenseIndex(fixture.StatModifiers.GetFirstDenseIndex(TargetEntityId)).ExpiresAtFrame);
    }

    [TestMethod]
    public void DodgeActivation_NoSourceEntity_DoesNothing()
    {
        var fixture = WithSimpleHealth();

        Assert.AreEqual(EffectOutcome.NoEffect, new DodgeActivation().Apply(fixture.Sourceless()));
        Assert.AreEqual(0, fixture.ComponentManager.GetPackedPool<DodgingComponent>().Count);
    }

    [TestMethod]
    public void Outcome_OfSeveralEntries_IsAppliedIfAnyLandedOtherwiseRefusedIfAnyWasRefused()
    {
        var fixture = WithSimpleHealth();
        StatusEffectImmunityEffects.GrantPermanent(fixture.ComponentManager.GetMultiPool<StatusEffectImmunityComponent>(), TargetEntityId, StatusEffectType.Burning);
        var refused = new StatusEffectGrant(StatusEffectType.Burning);
        var nothing = new DirectHeal(PercentOfMaxHealth: 0.5f);
        var lands = new StatusEffectGrant(StatusEffectType.Poison);

        Assert.AreEqual(EffectOutcome.Refused, new Effect([nothing, refused]).Apply(fixture.Sourceless()));
        Assert.AreEqual(EffectOutcome.Applied, EffectSequence.Apply([new Effect([refused]), new Effect([lands])], fixture.Sourceless()));
        Assert.AreEqual(EffectOutcome.Applied, new Effect([new ChainedEffect(1f, [new Effect([lands])])]).Apply(fixture.Sourceless()));
    }

    [TestMethod]
    public void PoisonApplyStacks_AddsSeveralAtOnceAndStopsAtTheCap()
    {
        var fixture = WithSimpleHealth();
        var entityKeys = new EntityKeys();

        var first = PoisonEffects.ApplyStacks(fixture.ComponentManager, entityKeys, TargetEntityId, count: 250, ActionSource.Admin, durationInTicks: 5, now: 0, fixture.EventBus, TestPlayerQuery.NoPlayer);
        var second = PoisonEffects.ApplyStacks(fixture.ComponentManager, entityKeys, TargetEntityId, count: 250, ActionSource.Admin, durationInTicks: 5, now: 0, fixture.EventBus, TestPlayerQuery.NoPlayer);

        Assert.AreEqual(250, first);
        Assert.AreEqual(PoisonEffects.MaxStacks - 250, second);
        Assert.AreEqual(PoisonEffects.MaxStacks, fixture.ComponentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(TargetEntityId).StackCount);
    }
}
