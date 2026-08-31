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
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<BurningTimerComponent>(static (ref existing, incoming) => { });
        return componentManager;
    }

    /// <summary>Mirrors BurningModule.Configure's own registration -- the real path StatusEffectQueries reads through.</summary>
    private static StatusEffectDisplayRegistry CreateStatusEffectDisplays()
    {
        var displays = new StatusEffectDisplayRegistry();
        displays.Register(new TimerBasedStatusEffectDisplay<BurningTimerComponent>(StatusEffectType.Burning, BurningEffects.Glyph,
            burning => burning.FramesUntilNextTick + (burning.StackCount - 1) * BurningEffects.TickIntervalFrames));
        return displays;
    }

    [TestMethod]
    public void ApplyStack_EntityImmuneToBurning_DoesNotAddAStack()
    {
        var componentManager = CreateComponentManager();
        componentManager.RegisterMultiPool<StatusEffectImmunityComponent>();
        componentManager.GetMultiPool<StatusEffectImmunityComponent>().Add(0, new StatusEffectImmunityComponent(StatusEffectType.Burning, remainingDurationFrames: null));

        BurningEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin);

        Assert.AreEqual(0, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(), componentManager, 0, StatusEffectType.Burning));
        Assert.IsFalse(componentManager.GetPackedPool<BurningTimerComponent>().Has(0));
    }

    [TestMethod]
    public void ApplyStack_EntityImmuneToPoisonOnly_StillCatchesFire()
    {
        var componentManager = CreateComponentManager();
        componentManager.RegisterMultiPool<StatusEffectImmunityComponent>();
        componentManager.GetMultiPool<StatusEffectImmunityComponent>().Add(0, new StatusEffectImmunityComponent(StatusEffectType.Poison, remainingDurationFrames: null));

        BurningEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(), componentManager, 0, StatusEffectType.Burning));
    }

    [TestMethod]
    public void ApplyStack_AddsAStack()
    {
        var componentManager = CreateComponentManager();

        BurningEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(), componentManager, 0, StatusEffectType.Burning));
    }

    [TestMethod]
    public void ApplyStack_FirstStack_CreatesTimerWithFreshCountdown()
    {
        var componentManager = CreateComponentManager();

        BurningEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin);

        var timer = componentManager.GetPackedPool<BurningTimerComponent>().GetReadonly(0);
        Assert.AreEqual(BurningEffects.TickIntervalFrames, timer.FramesUntilNextTick);
    }

    [TestMethod]
    public void ApplyStack_NeverExceedsMaxStacks()
    {
        var componentManager = CreateComponentManager();

        for (var i = 0; i < BurningEffects.MaxStacks + 5; i++)
        {
            BurningEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin);
        }

        Assert.AreEqual(BurningEffects.MaxStacks, StatusEffectQueries.CountStacks(CreateStatusEffectDisplays(), componentManager, 0, StatusEffectType.Burning));
    }

    [TestMethod]
    public void ApplyStack_WhileAlreadyBurning_DoesNotResetCountdown()
    {
        var componentManager = CreateComponentManager();
        BurningEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin);
        componentManager.GetPackedPool<BurningTimerComponent>().TryUpdate(0, static (ref BurningTimerComponent t) => t.FramesUntilNextTick = 5);

        BurningEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin);

        var timer = componentManager.GetPackedPool<BurningTimerComponent>().GetReadonly(0);
        Assert.AreEqual(5, timer.FramesUntilNextTick);
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
        BurningEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin);

        BurningEffects.ApplyStack(componentManager, 0, StatusEffectSource.FromEntity(42));

        var timer = componentManager.GetPackedPool<BurningTimerComponent>().GetReadonly(0);
        Assert.AreEqual(StatusEffectSource.Admin, timer.Source);
        Assert.AreEqual(2, timer.StackCount);
    }
}
