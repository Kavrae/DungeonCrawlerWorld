using Engine.ECS.Systems;
using Engine.ECS.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Tests.Modules.StatusEffects;

[TestClass]
public sealed class TimerBasedStatusEffectDisplayTests
{
    private const int EntityId = 0;

    private static ComponentManager CreateComponentManagerWithPoisonTimerPool()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4));
        return componentManager;
    }

    private static TimerBasedStatusEffectDisplay<PoisonTimerComponent> CreatePoisonDisplay(ComponentManager componentManager) =>
        new(StatusEffectType.Poison, PoisonEffects.Glyph, componentManager.GetPackedPool<PoisonTimerComponent>(),
            (poison, now) => FrameDeadline.Remaining(poison.NextTickFrame, now) + (poison.RemainingDurationTicks - 1) * PoisonEffects.TickIntervalFrames);

    [TestMethod]
    public void GetRemainingDurationFrames_TimerNotPresent_ReturnsNull()
    {
        var componentManager = CreateComponentManagerWithPoisonTimerPool();
        var display = CreatePoisonDisplay(componentManager);

        Assert.IsNull(display.GetRemainingDurationFrames(EntityId, now: 0));
    }

    [TestMethod]
    public void GetRemainingDurationFrames_TimerPresent_MatchesFormula()
    {
        var componentManager = CreateComponentManagerWithPoisonTimerPool();
        componentManager.GetPackedPool<PoisonTimerComponent>().Add(EntityId, new PoisonTimerComponent(nextTickFrame: 30, stackCount: 1, remainingDurationTicks: 3, ActionSource.Admin));
        var display = CreatePoisonDisplay(componentManager);

        // FramesUntilNextTick 30 + (RemainingDurationTicks 3 - 1) * TickIntervalFrames 60 = 150.
        Assert.AreEqual(150, display.GetRemainingDurationFrames(EntityId, now: 0));
    }
}
