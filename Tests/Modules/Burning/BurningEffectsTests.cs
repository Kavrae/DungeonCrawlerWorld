using Engine.Events;
using Engine.ECS.Systems;
using Engine.ECS.Components;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Modules.Burning;

[TestClass]
public sealed class BurningEffectsTests
{
    private static ComponentManager CreateComponentManager()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));
        return componentManager;
    }

    /// <summary>Mirrors BurningModule.Configure's own registration -- the real path StatusEffectQueries reads through.</summary>
    private static StatusEffectDisplayRegistry CreateStatusEffectDisplays(ComponentManager componentManager)
    {
        var displays = new StatusEffectDisplayRegistry();
        displays.Register(new TimerBasedStatusEffectDisplay<BurningTimerComponent>(StatusEffectType.Burning, BurningEffects.Glyph, componentManager.GetPackedPool<BurningTimerComponent>(),
            (burning, now) => FrameDeadline.Remaining(burning.NextTickFrame, now) + (burning.StackCount - 1) * BurningEffects.TickIntervalFrames));
        return displays;
    }

    [TestMethod]
    public void ApplyStack_EntityImmuneToBurning_DoesNotAddAStack()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetMultiPool<StatusEffectImmunityComponent>().Add(0, new StatusEffectImmunityComponent(StatusEffectType.Burning, expiresAtFrame: FrameDeadline.Never));

        BurningEffects.ApplyStack(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(0, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(componentManager), 0, StatusEffectType.Burning));
        Assert.IsFalse(componentManager.GetPackedPool<BurningTimerComponent>().Has(0));
    }

    [TestMethod]
    public void ApplyStack_EntityImmuneToPoisonOnly_StillCatchesFire()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetMultiPool<StatusEffectImmunityComponent>().Add(0, new StatusEffectImmunityComponent(StatusEffectType.Poison, expiresAtFrame: FrameDeadline.Never));

        BurningEffects.ApplyStack(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(componentManager), 0, StatusEffectType.Burning));
    }

    [TestMethod]
    public void ApplyStack_AddsAStack()
    {
        var componentManager = CreateComponentManager();

        BurningEffects.ApplyStack(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(componentManager), 0, StatusEffectType.Burning));
    }

    [TestMethod]
    public void ApplyStack_FirstStack_CreatesTimerWithFreshCountdown()
    {
        var componentManager = CreateComponentManager();

        BurningEffects.ApplyStack(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        var timer = componentManager.GetPackedPool<BurningTimerComponent>().GetReadonly(0);
        Assert.AreEqual(FrameDeadline.AfterStaggered(0, BurningEffects.TickIntervalFrames, 0), timer.NextTickFrame);
    }

    [TestMethod]
    public void ApplyStack_NeverExceedsMaxStacks()
    {
        var componentManager = CreateComponentManager();

        for (var i = 0; i < BurningEffects.MaxStacks + 5; i++)
        {
            BurningEffects.ApplyStack(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);
        }

        Assert.AreEqual(BurningEffects.MaxStacks, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(componentManager), 0, StatusEffectType.Burning));
    }

    [TestMethod]
    public void ApplyStack_WhileAlreadyBurning_DoesNotResetCountdown()
    {
        var componentManager = CreateComponentManager();
        BurningEffects.ApplyStack(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);
        componentManager.GetPackedPool<BurningTimerComponent>().TryUpdate(0, static (ref BurningTimerComponent t) => t.NextTickFrame = 5);

        BurningEffects.ApplyStack(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        var timer = componentManager.GetPackedPool<BurningTimerComponent>().GetReadonly(0);
        Assert.AreEqual(5u, timer.NextTickFrame);
    }

    /// <summary>
    /// BurningTimerComponent.Source is set once, on the 0-to-1 transition, and never overwritten
    /// by a later top-off -- whoever started the burn is attributed for its whole duration, the
    /// same "first applier wins" rule PoisonEffects.ApplyStack already uses for its own Source.
    /// </summary>
    [TestMethod]
    public void ApplyStack_SecondApplicationFromDifferentSource_DoesNotChangeSource()
    {
        var componentManager = CreateComponentManager();
        BurningEffects.ApplyStack(componentManager, 0, ActionSource.Admin, now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        BurningEffects.ApplyStack(componentManager, 0, TestSources.Entity(42), now: 0, new EventBus(), TestPlayerQuery.NoPlayer);

        var timer = componentManager.GetPackedPool<BurningTimerComponent>().GetReadonly(0);
        Assert.AreEqual(ActionSource.Admin, timer.Source);
        Assert.AreEqual(2, timer.StackCount);
    }
}
