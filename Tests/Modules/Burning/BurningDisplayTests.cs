using Engine.ECS.Components;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.World;

namespace Tests.Modules.Burning;

[TestClass]
public sealed class BurningDisplayTests
{
    private const int EntityId = 0;

    private static (ComponentManager ComponentManager, BurningDisplay Display) Build()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));
        var display = new BurningDisplay(componentManager.GetPackedPool<BurningTimerComponent>(), componentManager.GetMultiPool<BodyPartBurningTimerComponent>());
        return (componentManager, display);
    }

    [TestMethod]
    public void NotBurning_ShowsNoStacksAndNoDuration()
    {
        var (_, display) = Build();

        Assert.AreEqual(0, display.GetStackCount(EntityId));
        Assert.IsNull(display.GetRemainingDurationFrames(EntityId, now: 0));
    }

    [TestMethod]
    public void OnlyBodyPartsBurning_ShowsTheHighestPartsStacksAndTheLongestBurn()
    {
        var (componentManager, display) = Build();
        var partTimers = componentManager.GetMultiPool<BodyPartBurningTimerComponent>();
        partTimers.Add(EntityId, new BodyPartBurningTimerComponent(partId: 0, stackCount: 3, nextTickFrame: 60, ActionSource.Admin));
        partTimers.Add(EntityId, new BodyPartBurningTimerComponent(partId: 1, stackCount: 8, nextTickFrame: 30, ActionSource.Admin));

        Assert.AreEqual(8, display.GetStackCount(EntityId));
        Assert.AreEqual(30 + 7 * BurningEffects.TickIntervalFrames, display.GetRemainingDurationFrames(EntityId, now: 0));
    }

    [TestMethod]
    public void EntityAndBodyPartBurning_ShowsWhicheverIsHigherNotTheirSum()
    {
        var (componentManager, display) = Build();
        componentManager.GetPackedPool<BurningTimerComponent>().Add(EntityId, new BurningTimerComponent(nextTickFrame: 60, stackCount: 4, ActionSource.Admin));
        componentManager.GetMultiPool<BodyPartBurningTimerComponent>().Add(EntityId, new BodyPartBurningTimerComponent(partId: 0, stackCount: 2, nextTickFrame: 60, ActionSource.Admin));

        Assert.AreEqual(4, display.GetStackCount(EntityId));
        Assert.AreEqual(60 + 3 * BurningEffects.TickIntervalFrames, display.GetRemainingDurationFrames(EntityId, now: 0));
    }
}
