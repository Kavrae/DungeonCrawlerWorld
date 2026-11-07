using Engine.Events;
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
    private static ComponentManager CreateComponentManager() =>
        BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));

    /// <summary>Mirrors PoisonModule.Configure's own registration -- the real path StatusEffectQueries reads through.</summary>
    private static StatusEffectDisplayRegistry CreateStatusEffectDisplays(ComponentManager componentManager)
    {
        var displays = new StatusEffectDisplayRegistry();
        displays.Register(new TimerBasedStatusEffectDisplay<PoisonTimerComponent>(StatusEffectType.Poison, PoisonEffects.Glyph, PoisonEffects.MaxStacks, componentManager.GetPackedPool<PoisonTimerComponent>(),
            (poison, now) => TimedStatusEffectStacks.RemainingFrames(poison, now, PoisonEffects.TickIntervalFrames)));
        return displays;
    }

    private static StatusEffectStacksApplied Poison(ComponentManager componentManager, ushort? durationTicks, int count = 1) =>
        PoisonEffects.ApplyStacks(componentManager, entityId: 0, count, ActionSource.Admin, durationTicks, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

    [TestMethod]
    public void ApplyStacks_EntityImmuneToPoison_IsRefusedAndAddsNothing()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetMultiPool<StatusEffectImmunityComponent>().Add(0, new StatusEffectImmunityComponent(StatusEffectType.Poison, expiresAtFrame: FrameDeadline.Never));

        var applied = Poison(componentManager, durationTicks: 5);

        Assert.IsTrue(applied.RefusedByImmunity);
        Assert.IsFalse(componentManager.GetPackedPool<PoisonTimerComponent>().Has(0));
    }

    [TestMethod]
    public void ApplyStacks_EntityImmuneToBurningOnly_StillGetsPoisoned()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetMultiPool<StatusEffectImmunityComponent>().Add(0, new StatusEffectImmunityComponent(StatusEffectType.Burning, expiresAtFrame: FrameDeadline.Never));

        Poison(componentManager, durationTicks: 5);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(componentManager), 0, StatusEffectType.Poison));
    }

    [TestMethod]
    public void ApplyStacks_FirstStacks_CreateTimerWithGivenDurationAndFreshCountdown()
    {
        var componentManager = CreateComponentManager();

        Poison(componentManager, durationTicks: 5, count: 3);

        var timer = componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0);
        Assert.AreEqual(FrameDeadline.AfterStaggered(0, PoisonEffects.TickIntervalFrames, 0), timer.NextTickFrame);
        Assert.AreEqual(5, timer.RemainingDurationTicks);
        Assert.AreEqual(3, timer.StackCount);
        Assert.AreEqual(ActionSource.Admin, timer.Source);
    }

    [TestMethod]
    public void ApplyStacks_NoDuration_LastsTheDefaultUnscaled()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetMultiPool<StatModifierComponent>().Add(0, new StatModifierComponent(
            StatModifierTarget.IncomingDebuffDuration, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, canModify: false, magnitude: -0.9f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));

        Poison(componentManager, durationTicks: null);

        Assert.AreEqual(PoisonEffects.DefaultDurationTicks, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0).RemainingDurationTicks);
    }

    [TestMethod]
    public void ApplyStacks_NeverExceedsMaxStacks()
    {
        var componentManager = CreateComponentManager();

        var first = Poison(componentManager, durationTicks: 5, count: PoisonEffects.MaxStacks - 1);
        var second = Poison(componentManager, durationTicks: 5, count: 5);

        Assert.AreEqual(PoisonEffects.MaxStacks - 1, first.StacksLanded);
        Assert.AreEqual(1, second.StacksLanded);
        Assert.AreEqual(PoisonEffects.MaxStacks, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(componentManager), 0, StatusEffectType.Poison));
    }

    [TestMethod]
    public void ApplyStacks_WhileAlreadyPoisoned_DoesNotResetCountdown()
    {
        var componentManager = CreateComponentManager();
        Poison(componentManager, durationTicks: 5);
        componentManager.GetPackedPool<PoisonTimerComponent>().TrySet(0, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0) with { NextTickFrame = 5 });

        Poison(componentManager, durationTicks: 5);

        Assert.AreEqual(5u, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0).NextTickFrame);
    }

    [TestMethod]
    public void ApplyStacks_WhileAlreadyPoisoned_AddsToTheStackCount()
    {
        var componentManager = CreateComponentManager();
        Poison(componentManager, durationTicks: 5, count: 2);

        Poison(componentManager, durationTicks: 5, count: 3);

        Assert.AreEqual(5, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0).StackCount);
    }

    [TestMethod]
    public void ApplyStacks_LongerNewDuration_ExtendsRemainingDuration()
    {
        var componentManager = CreateComponentManager();
        Poison(componentManager, durationTicks: 3);

        Poison(componentManager, durationTicks: 10);

        Assert.AreEqual(10, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0).RemainingDurationTicks);
    }

    [TestMethod]
    public void ApplyStacks_ShorterNewDuration_DoesNotShortenRemainingDuration()
    {
        var componentManager = CreateComponentManager();
        Poison(componentManager, durationTicks: 10);

        Poison(componentManager, durationTicks: 3);

        Assert.AreEqual(10, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0).RemainingDurationTicks);
    }

    [TestMethod]
    public void RemainingFrames_IsTheRunningTickThenOneIntervalPerTickLeft()
    {
        var timer = new PoisonTimerComponent(nextTickFrame: 30, stackCount: 1, remainingDurationTicks: 4, ActionSource.Admin);

        Assert.AreEqual(20 + 3 * PoisonEffects.TickIntervalFrames, TimedStatusEffectStacks.RemainingFrames(timer, now: 10, PoisonEffects.TickIntervalFrames));
    }
}
