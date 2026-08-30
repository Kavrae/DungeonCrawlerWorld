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
        componentManager.RegisterMultiPool<StatusEffectStack>();
        componentManager.RegisterPackedPool<BurningTimerComponent>(static (ref existing, incoming) => { });
        return componentManager;
    }

    [TestMethod]
    public void ApplyStack_EntityImmuneToBurning_DoesNotAddAStack()
    {
        var componentManager = CreateComponentManager();
        componentManager.RegisterMultiPool<StatusEffectImmunityComponent>();
        componentManager.GetMultiPool<StatusEffectImmunityComponent>().Add(0, new StatusEffectImmunityComponent(StatusEffectType.Burning, remainingDurationFrames: null));

        BurningEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin);

        Assert.AreEqual(0, StatusEffectQueries.CountStacks(componentManager.GetMultiPool<StatusEffectStack>(), 0, StatusEffectType.Burning));
        Assert.IsFalse(componentManager.GetPackedPool<BurningTimerComponent>().Has(0));
    }

    [TestMethod]
    public void ApplyStack_EntityImmuneToPoisonOnly_StillCatchesFire()
    {
        var componentManager = CreateComponentManager();
        componentManager.RegisterMultiPool<StatusEffectImmunityComponent>();
        componentManager.GetMultiPool<StatusEffectImmunityComponent>().Add(0, new StatusEffectImmunityComponent(StatusEffectType.Poison, remainingDurationFrames: null));

        BurningEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(componentManager.GetMultiPool<StatusEffectStack>(), 0, StatusEffectType.Burning));
    }

    [TestMethod]
    public void ApplyStack_AddsAStack()
    {
        var componentManager = CreateComponentManager();

        BurningEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(componentManager.GetMultiPool<StatusEffectStack>(), 0, StatusEffectType.Burning));
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

        Assert.AreEqual(BurningEffects.MaxStacks, StatusEffectQueries.CountStacks(componentManager.GetMultiPool<StatusEffectStack>(), 0, StatusEffectType.Burning));
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
}
