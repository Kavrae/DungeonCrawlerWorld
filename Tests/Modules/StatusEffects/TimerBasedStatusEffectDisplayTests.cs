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
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        componentManager.RegisterPackedPool<PoisonTimerComponent>(static (ref existing, incoming) => { });
        return componentManager;
    }

    private static TimerBasedStatusEffectDisplay<PoisonTimerComponent> CreatePoisonDisplay() =>
        new(StatusEffectType.Poison, PoisonEffects.Glyph,
            poison => poison.FramesUntilNextTick + (poison.RemainingDurationTicks - 1) * PoisonEffects.TickIntervalFrames);

    [TestMethod]
    public void GetRemainingDurationFrames_TimerNotPresent_ReturnsNull()
    {
        var componentManager = CreateComponentManagerWithPoisonTimerPool();
        var display = CreatePoisonDisplay();

        Assert.IsNull(display.GetRemainingDurationFrames(componentManager, EntityId));
    }

    [TestMethod]
    public void GetRemainingDurationFrames_TimerPresent_MatchesFormula()
    {
        var componentManager = CreateComponentManagerWithPoisonTimerPool();
        componentManager.GetPackedPool<PoisonTimerComponent>().Add(EntityId, new PoisonTimerComponent(framesUntilNextTick: 30, stackCount: 1, remainingDurationTicks: 3, StatusEffectSource.Admin));
        var display = CreatePoisonDisplay();

        // FramesUntilNextTick 30 + (RemainingDurationTicks 3 - 1) * TickIntervalFrames 60 = 150.
        Assert.AreEqual(150, display.GetRemainingDurationFrames(componentManager, EntityId));
    }
}
