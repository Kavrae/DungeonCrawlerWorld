using Engine.ECS.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Modules.Poison;

[TestClass]
public sealed class PoisonEffectsTests
{
    private static ComponentManager CreateComponentManager()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterMultiPool<StatusEffectStack>();
        componentManager.RegisterPackedPool<PoisonTimerComponent>(static (ref existing, incoming) => { });
        return componentManager;
    }

    [TestMethod]
    public void ApplyStack_AddsAStack()
    {
        var componentManager = CreateComponentManager();

        PoisonEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin, durationInTicks: 5);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(componentManager.GetMultiPool<StatusEffectStack>(), 0, StatusEffectType.Poison));
    }

    [TestMethod]
    public void ApplyStack_FirstStack_CreatesTimerWithGivenDurationAndFreshCountdown()
    {
        var componentManager = CreateComponentManager();

        PoisonEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin, durationInTicks: 5);

        var timer = componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0);
        Assert.AreEqual(PoisonEffects.TickIntervalFrames, timer.FramesUntilNextTick);
        Assert.AreEqual(5, timer.RemainingDurationTicks);
        Assert.AreEqual(1, timer.StackCount);
        Assert.AreEqual(StatusEffectSource.Admin, timer.Source);
    }

    [TestMethod]
    public void ApplyStack_NeverExceedsMaxStacks()
    {
        var componentManager = CreateComponentManager();

        for (var i = 0; i < PoisonEffects.MaxStacks + 5; i++)
        {
            PoisonEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin, durationInTicks: 5);
        }

        Assert.AreEqual(PoisonEffects.MaxStacks, StatusEffectQueries.CountStacks(componentManager.GetMultiPool<StatusEffectStack>(), 0, StatusEffectType.Poison));
    }

    [TestMethod]
    public void ApplyStack_WhileAlreadyPoisoned_DoesNotResetCountdown()
    {
        var componentManager = CreateComponentManager();
        PoisonEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin, durationInTicks: 5);
        componentManager.GetPackedPool<PoisonTimerComponent>().TryUpdate(0, static (ref PoisonTimerComponent t) => t.FramesUntilNextTick = 5);

        PoisonEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin, durationInTicks: 5);

        var timer = componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0);
        Assert.AreEqual(5, timer.FramesUntilNextTick);
    }

    [TestMethod]
    public void ApplyStack_WhileAlreadyPoisoned_IncrementsStackCount()
    {
        var componentManager = CreateComponentManager();
        PoisonEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin, durationInTicks: 5);

        PoisonEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin, durationInTicks: 5);

        Assert.AreEqual(2, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0).StackCount);
    }

    [TestMethod]
    public void ApplyStack_LongerNewDuration_ExtendsRemainingDuration()
    {
        var componentManager = CreateComponentManager();
        PoisonEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin, durationInTicks: 3);

        PoisonEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin, durationInTicks: 10);

        Assert.AreEqual(10, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0).RemainingDurationTicks);
    }

    [TestMethod]
    public void ApplyStack_ShorterNewDuration_DoesNotShortenRemainingDuration()
    {
        var componentManager = CreateComponentManager();
        PoisonEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin, durationInTicks: 10);

        PoisonEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin, durationInTicks: 3);

        Assert.AreEqual(10, componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0).RemainingDurationTicks);
    }

    /// <summary>Mirrors the player's own test seeding (FloorBuilder.CreatePlayer): duration is max-of, not additive, so 10 applications of 5 ticks each ends at 5 ticks remaining, not 50.</summary>
    [TestMethod]
    public void ApplyStack_TenApplicationsOfFiveTickDuration_EndsAtTenStacksAndFiveTicks()
    {
        var componentManager = CreateComponentManager();

        for (var i = 0; i < 10; i++)
        {
            PoisonEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin, durationInTicks: 5);
        }

        var timer = componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(0);
        Assert.AreEqual(10, timer.StackCount);
        Assert.AreEqual(5, timer.RemainingDurationTicks);
    }
}
