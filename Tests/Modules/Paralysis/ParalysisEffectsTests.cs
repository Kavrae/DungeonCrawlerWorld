using Engine.Events;
using Engine.ECS.Systems;
using Engine.ECS.Components;
using Game.Modules.Core.Components;
using Game.Modules.Paralysis;
using Game.Modules.Paralysis.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Modules.Paralysis;

[TestClass]
public sealed class ParalysisEffectsTests
{
    private static ComponentManager CreateComponentManager()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));
        componentManager.GetPackedPool<ActionLockComponent>().Add(0, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, unlockedAtFrame: 0));
        return componentManager;
    }

    /// <summary>Mirrors ParalysisModule.Configure's own registration -- the real path StatusEffectQueries reads through.</summary>
    private static StatusEffectDisplayRegistry CreateStatusEffectDisplays()
    {
        var displays = new StatusEffectDisplayRegistry();
        displays.Register(new TimerBasedStatusEffectDisplay<ParalysisTimerComponent>(StatusEffectType.Paralysis, ParalysisEffects.Glyph,
            (paralysis, now) => FrameDeadline.Remaining(paralysis.ExpiresAtFrame, now)));
        return displays;
    }

    [TestMethod]
    public void Apply_EntityImmuneToParalysis_DoesNotAddAStackOrLockActions()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetMultiPool<StatusEffectImmunityComponent>().Add(0, new StatusEffectImmunityComponent(StatusEffectType.Paralysis, expiresAtFrame: FrameDeadline.Never));

        ParalysisEffects.Apply(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(0, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(), componentManager, 0, StatusEffectType.Paralysis));
        Assert.IsFalse(componentManager.GetPackedPool<ParalysisTimerComponent>().Has(0));
        Assert.AreEqual(0u, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(0).UnlockedAtFrame);
    }

    [TestMethod]
    public void Apply_EntityImmuneToPoisonOnly_StillGetsParalyzed()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetMultiPool<StatusEffectImmunityComponent>().Add(0, new StatusEffectImmunityComponent(StatusEffectType.Poison, expiresAtFrame: FrameDeadline.Never));

        ParalysisEffects.Apply(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(), componentManager, 0, StatusEffectType.Paralysis));
    }

    [TestMethod]
    public void Apply_NewEntity_AddsAStack()
    {
        var componentManager = CreateComponentManager();

        ParalysisEffects.Apply(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(), componentManager, 0, StatusEffectType.Paralysis));
    }

    [TestMethod]
    public void Apply_NewEntity_CreatesTimerWithDurationFrames()
    {
        var componentManager = CreateComponentManager();

        ParalysisEffects.Apply(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        var timer = componentManager.GetPackedPool<ParalysisTimerComponent>().GetReadonly(0);
        Assert.AreEqual(ParalysisEffects.DurationFrames, timer.ExpiresAtFrame);
    }

    [TestMethod]
    public void Apply_NewEntity_LocksActionLockComponentForDurationFrames()
    {
        var componentManager = CreateComponentManager();

        ParalysisEffects.Apply(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        var actionLock = componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(0);
        Assert.AreEqual(ParalysisEffects.DurationFrames, actionLock.UnlockedAtFrame);
    }

    [TestMethod]
    public void Apply_WhileAlreadyParalyzed_DoesNotAddASecondStack()
    {
        var componentManager = CreateComponentManager();
        ParalysisEffects.Apply(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        ParalysisEffects.Apply(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(), componentManager, 0, StatusEffectType.Paralysis));
    }

    /// <summary>Refreshes to the greater of what remained and DurationFrames -- never additive, mirroring PoisonEffects.ApplyStack's own duration rule.</summary>
    [TestMethod]
    public void Apply_WhileAlreadyParalyzedWithLessTimeRemaining_RefreshesToDurationFrames()
    {
        var componentManager = CreateComponentManager();
        ParalysisEffects.Apply(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);
        componentManager.GetPackedPool<ParalysisTimerComponent>().TryUpdate(0, static (ref ParalysisTimerComponent t) => t.ExpiresAtFrame = 5);

        ParalysisEffects.Apply(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(ParalysisEffects.DurationFrames, componentManager.GetPackedPool<ParalysisTimerComponent>().GetReadonly(0).ExpiresAtFrame);
    }

    [TestMethod]
    public void Apply_WhileAlreadyParalyzedWithLessTimeRemaining_RefreshesActionLockToDurationFrames()
    {
        var componentManager = CreateComponentManager();
        ParalysisEffects.Apply(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);
        componentManager.GetPackedPool<ActionLockComponent>().TryUpdate(0, static (ref ActionLockComponent a) => a.UnlockedAtFrame = 5);

        ParalysisEffects.Apply(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(ParalysisEffects.DurationFrames, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(0).UnlockedAtFrame);
    }
}
