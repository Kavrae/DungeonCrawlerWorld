using Game.Blueprints;
using Engine.Events;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.ECS.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Modules.Poison;

[TestClass]
public sealed class PoisonEffectsTests
{
    private static ComponentManager CreateComponentManager()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));
        return componentManager;
    }

    private static ComponentManager CreateComponentManagerWithImmunity()
    {
        var componentManager = CreateComponentManager();
        return componentManager;
    }

    /// <summary>Mirrors PoisonModule.Configure's own registration -- the real path StatusEffectQueries reads through.</summary>
    private static StatusEffectDisplayRegistry CreateStatusEffectDisplays()
    {
        var displays = new StatusEffectDisplayRegistry();
        displays.Register(new TimerBasedStatusEffectDisplay<PoisonTimerComponent>(StatusEffectType.Poison, PoisonEffects.Glyph,
            (poison, now) => FrameDeadline.Remaining(poison.NextTickFrame, now) + (poison.RemainingDurationTicks - 1) * PoisonEffects.TickIntervalFrames));
        return displays;
    }

    [TestMethod]
    public void ApplyStack_EntityImmuneToPoison_DoesNotAddAStack()
    {
        var componentManager = CreateComponentManagerWithImmunity();
        componentManager.GetMultiPool<StatusEffectImmunityComponent>().Add(0, new StatusEffectImmunityComponent(StatusEffectType.Poison, expiresAtFrame: FrameDeadline.Never));

        PoisonEffects.ApplyStack(componentManager, new EntityKeys(), 0, ActionSource.Admin, durationInTicks: 5, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(0, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(), componentManager, 0, StatusEffectType.Poison));
        Assert.IsFalse(componentManager.GetPackedPool<PoisonTimerComponent>().Has(0));
    }

    [TestMethod]
    public void ApplyStack_EntityImmuneToBurningOnly_StillGetsPoisoned()
    {
        var componentManager = CreateComponentManagerWithImmunity();
        componentManager.GetMultiPool<StatusEffectImmunityComponent>().Add(0, new StatusEffectImmunityComponent(StatusEffectType.Burning, expiresAtFrame: FrameDeadline.Never));

        PoisonEffects.ApplyStack(componentManager, new EntityKeys(), 0, ActionSource.Admin, durationInTicks: 5, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(), componentManager, 0, StatusEffectType.Poison));
    }

    [TestMethod]
    public void ApplyStack_OutgoingDebuffDurationModifierOnSource_ScalesDuration()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetMultiPool<StatModifierComponent>().Add(1, new StatModifierComponent(
            StatModifierTarget.OutgoingDebuffDuration, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, canModify: false, magnitude: 1.0f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));

        var entityKeys = new EntityKeys();
        entityKeys.Issue(0);
        entityKeys.Issue(1);

        PoisonEffects.ApplyStack(componentManager, entityKeys, entityId: 0, ActionSource.FromEntity(componentManager, entityKeys, 1, creatures: new BlueprintRegistry()), durationInTicks: 5, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(10, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0).RemainingDurationTicks, "5 * (1 + 1.0) = 10.");
    }

    [TestMethod]
    public void ApplyStack_IncomingDebuffDurationModifierOnTarget_ScalesDuration()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetMultiPool<StatModifierComponent>().Add(0, new StatModifierComponent(
            StatModifierTarget.IncomingDebuffDuration, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, canModify: false, magnitude: -0.5f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));

        PoisonEffects.ApplyStack(componentManager, new EntityKeys(), entityId: 0, ActionSource.Admin, durationInTicks: 10, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(5, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0).RemainingDurationTicks, "10 * (1 - 0.5) = 5.");
    }

    [TestMethod]
    public void ApplyStack_AddsAStack()
    {
        var componentManager = CreateComponentManager();

        PoisonEffects.ApplyStack(componentManager, new EntityKeys(), 0, ActionSource.Admin, durationInTicks: 5, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(), componentManager, 0, StatusEffectType.Poison));
    }

    [TestMethod]
    public void ApplyStack_FirstStack_CreatesTimerWithGivenDurationAndFreshCountdown()
    {
        var componentManager = CreateComponentManager();

        PoisonEffects.ApplyStack(componentManager, new EntityKeys(), 0, ActionSource.Admin, durationInTicks: 5, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        var timer = componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0);
        Assert.AreEqual(FrameDeadline.AfterStaggered(0, PoisonEffects.TickIntervalFrames, 0), timer.NextTickFrame);
        Assert.AreEqual(5, timer.RemainingDurationTicks);
        Assert.AreEqual(1, timer.StackCount);
        Assert.AreEqual(ActionSource.Admin, timer.Source);
    }

    [TestMethod]
    public void ApplyStack_NeverExceedsMaxStacks()
    {
        var componentManager = CreateComponentManager();

        for (var i = 0; i < PoisonEffects.MaxStacks + 5; i++)
        {
            PoisonEffects.ApplyStack(componentManager, new EntityKeys(), 0, ActionSource.Admin, durationInTicks: 5, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);
        }

        Assert.AreEqual(PoisonEffects.MaxStacks, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(), componentManager, 0, StatusEffectType.Poison));
    }

    [TestMethod]
    public void ApplyStack_WhileAlreadyPoisoned_DoesNotResetCountdown()
    {
        var componentManager = CreateComponentManager();
        PoisonEffects.ApplyStack(componentManager, new EntityKeys(), 0, ActionSource.Admin, durationInTicks: 5, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);
        componentManager.GetPackedPool<PoisonTimerComponent>().TryUpdate(0, static (ref PoisonTimerComponent t) => t.NextTickFrame = 5);

        PoisonEffects.ApplyStack(componentManager, new EntityKeys(), 0, ActionSource.Admin, durationInTicks: 5, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        var timer = componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0);
        Assert.AreEqual(5u, timer.NextTickFrame);
    }

    [TestMethod]
    public void ApplyStack_WhileAlreadyPoisoned_IncrementsStackCount()
    {
        var componentManager = CreateComponentManager();
        PoisonEffects.ApplyStack(componentManager, new EntityKeys(), 0, ActionSource.Admin, durationInTicks: 5, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        PoisonEffects.ApplyStack(componentManager, new EntityKeys(), 0, ActionSource.Admin, durationInTicks: 5, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(2, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0).StackCount);
    }

    [TestMethod]
    public void ApplyStack_LongerNewDuration_ExtendsRemainingDuration()
    {
        var componentManager = CreateComponentManager();
        PoisonEffects.ApplyStack(componentManager, new EntityKeys(), 0, ActionSource.Admin, durationInTicks: 3, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        PoisonEffects.ApplyStack(componentManager, new EntityKeys(), 0, ActionSource.Admin, durationInTicks: 10, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(10, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0).RemainingDurationTicks);
    }

    [TestMethod]
    public void ApplyStack_ShorterNewDuration_DoesNotShortenRemainingDuration()
    {
        var componentManager = CreateComponentManager();
        PoisonEffects.ApplyStack(componentManager, new EntityKeys(), 0, ActionSource.Admin, durationInTicks: 10, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        PoisonEffects.ApplyStack(componentManager, new EntityKeys(), 0, ActionSource.Admin, durationInTicks: 3, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(10, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0).RemainingDurationTicks);
    }

    /// <summary>Mirrors the player's own test seeding (FloorBuilder.CreatePlayer): duration is max-of, not additive, so 10 applications of 5 ticks each ends at 5 ticks remaining, not 50.</summary>
    [TestMethod]
    public void ApplyStack_TenApplicationsOfFiveTickDuration_EndsAtTenStacksAndFiveTicks()
    {
        var componentManager = CreateComponentManager();

        for (var i = 0; i < 10; i++)
        {
            PoisonEffects.ApplyStack(componentManager, new EntityKeys(), 0, ActionSource.Admin, durationInTicks: 5, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);
        }

        var timer = componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0);
        Assert.AreEqual(10, timer.StackCount);
        Assert.AreEqual(5, timer.RemainingDurationTicks);
    }
}
